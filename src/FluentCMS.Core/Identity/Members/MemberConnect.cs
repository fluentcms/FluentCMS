using System.Collections.Immutable;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity.Oidc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace FluentCMS.Core.Identity.Members;

// What the endpoints of the authorization server do for a client of the member pool. The client names the pool: its
// authorize request is answered from the member cookie, never the admin's, and its tokens are a member's, with the
// members audience and the scopes of AuthorizationServer.MemberScopes. A code or refresh token says whose it is by its
// kind, and is redeemed only through a client of the pool it was issued in.
internal static class MemberConnect
{
    private const string Refused = "The sign-in was refused.";

    public static bool IsMemberClient(ImmutableDictionary<string, JsonElement> properties) => IsSet(properties, AuthorizationServer.MembersProperty);

    public static bool IsMemberToken(ClaimsPrincipal? principal) =>
        principal?.GetClaim(CmsClaimTypes.Kind) == nameof(PrincipalKind.Member);

    // The member signs in on the member pages, which hold the member cookie; without it the request goes there and comes
    // back. So does a member whose sign-in is older than the client or the server accepts.
    public static async Task<IResult> AuthorizeAsync(HttpContext context, OpenIddictRequest request)
    {
        var signIn = context.RequestServices.GetRequiredService<MemberSignInManager>();
        var (member, cookie) = await signIn.SignedInAsync();
        var parameters = context.Request.HasFormContentType ? context.Request.Form.ToList() : context.Request.Query.ToList();
        var afterSignIn = context.Request.PathBase + context.Request.Path + QueryString.Create(ConnectEndpoints.WithoutReauthentication(parameters));

        if (member is null)
        {
            return request.HasPromptValue(PromptValues.None)
                ? ConnectEndpoints.Refuse(Errors.LoginRequired, "The member is not signed in.")
                : Results.Challenge(new AuthenticationProperties { RedirectUri = afterSignIn }, [MemberAuthentication.CookieScheme]);
        }

        var now = context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        var signedInAt = CmsSignInManager.SignedInAt(cookie.Properties!) ?? now;
        var sessionCap = context.RequestServices.GetRequiredService<ClientOptions>().AbsoluteSessionLifetime;
        if (request.HasPromptValue(PromptValues.Login)
            || (request.MaxAge is { } maxAge && now - signedInAt > TimeSpan.FromSeconds(maxAge))
            || (sessionCap is { } cap && now - signedInAt > cap))
        {
            if (request.HasPromptValue(PromptValues.None))
            {
                return ConnectEndpoints.Refuse(Errors.LoginRequired, "The sign-in is older than the client or the server accepts.");
            }

            await signIn.SignOutAsync();
            return Results.Challenge(new AuthenticationProperties { RedirectUri = afterSignIn }, [MemberAuthentication.CookieScheme]);
        }

        var methods = cookie.Principal!.FindAll(Claims.AuthenticationMethodReference).Select(claim => claim.Value);
        var identity = TokenClaims.Identity()
            .SetClaim(Claims.AuthenticationTime, signedInAt.ToUnixTimeSeconds())
            .SetClaim(TokenClaims.SessionStart, now.ToUnixTimeSeconds());
        TokenClaims.ForMember(identity, member, await signIn.UserManager.GetSecurityStampAsync(member), request.GetScopes(), methods);
        return Results.SignIn(new ClaimsPrincipal(identity), properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    // The code or refresh token keeps its scopes and session; the member is read again, so one who is deactivated, locked
    // out or deleted, or whose password changed since, gets no new token.
    public static async Task<IResult> RedeemAsync(HttpContext context, OpenIddictRequest request, ClaimsPrincipal token)
    {
        var signIn = context.RequestServices.GetRequiredService<MemberSignInManager>();
        var member = token.GetClaim(Claims.Subject) is { } subject ? await signIn.UserManager.FindByIdAsync(subject) : null;
        if (member is null || !await signIn.MaySignInAsync(member))
        {
            return ConnectEndpoints.Refuse(Errors.InvalidGrant, "The member can no longer sign in.");
        }

        var stamp = await signIn.UserManager.GetSecurityStampAsync(member);
        if (!string.Equals(token.GetClaim(TokenClaims.SecurityStamp), stamp, StringComparison.Ordinal))
        {
            return ConnectEndpoints.Refuse(Errors.InvalidGrant, "The member's credentials changed since the token was issued.");
        }

        var identity = TokenClaims.Identity(token);
        var now = context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        if (!long.TryParse(identity.GetClaim(TokenClaims.SessionStart), NumberStyles.None, CultureInfo.InvariantCulture, out var started))
        {
            identity.SetClaim(TokenClaims.SessionStart, started = now.ToUnixTimeSeconds());
        }

        if (context.RequestServices.GetRequiredService<ClientOptions>().AbsoluteSessionLifetime is { } cap
            && now - DateTimeOffset.FromUnixTimeSeconds(started) > cap)
        {
            return ConnectEndpoints.Refuse(Errors.InvalidGrant, "The session has reached its absolute lifetime; the member signs in again.");
        }

        // The client is read again: one that the configuration moved to the admin pool redeems no member's token.
        var clients = context.RequestServices.GetRequiredService<IOpenIddictApplicationManager>();
        var client = (request.ClientId ?? token.GetPresenters().FirstOrDefault()) is { } clientId
            ? await clients.FindByClientIdAsync(clientId, context.RequestAborted)
            : null;
        if (client is null || !IsMemberClient(await clients.GetPropertiesAsync(client, context.RequestAborted)))
        {
            return ConnectEndpoints.Refuse(Errors.InvalidGrant, "The token was issued to a client of the member pool, which this client is not.");
        }

        TokenClaims.ForMember(identity, member, stamp, token.GetScopes());
        return Results.SignIn(new ClaimsPrincipal(identity), properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    // The direct login (Q4): grant_type=member_password with username and password, from a client of the member pool
    // that has AllowDirectLogin, while cms.Members.AllowDirectLogin is on. Every refusal is invalid_grant with one
    // description, so that the answer tells neither why nor whether the address is a member's; the reason is in the log.
    // A wrong password counts towards the member's lockout.
    public static async Task<IResult> DirectLoginAsync(HttpContext context, OpenIddictRequest request)
    {
        var services = context.RequestServices;
        var clients = services.GetRequiredService<IOpenIddictApplicationManager>();
        var client = request.ClientId is { } clientId ? await clients.FindByClientIdAsync(clientId, context.RequestAborted) : null;
        var properties = client is null ? ImmutableDictionary<string, JsonElement>.Empty : await clients.GetPropertiesAsync(client, context.RequestAborted);

        var reason = !services.GetRequiredService<MemberOptions>().AllowDirectLogin ? "the direct login is off for the instance"
            : !IsMemberClient(properties) ? "the client is not of the member pool"
            : !IsSet(properties, AuthorizationServer.DirectLoginProperty) ? "the direct login is off for the client"
            : string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password) ? "the request has no username or no password"
            : null;

        var signIn = services.GetRequiredService<MemberSignInManager>();
        var member = reason is null ? await signIn.DirectLoginAsync(request.Username!.Trim(), request.Password!) : null;
        if (member is null)
        {
            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(MemberConnect));
            MemberLog.DirectLoginRefused(logger, request.ClientId, context.Connection.RemoteIpAddress, reason ?? "the address or the password was refused");
            return ConnectEndpoints.Refuse(Errors.InvalidGrant, Refused);
        }

        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var identity = TokenClaims.Identity()
            .SetClaim(Claims.AuthenticationTime, now.ToUnixTimeSeconds())
            .SetClaim(TokenClaims.SessionStart, now.ToUnixTimeSeconds());
        TokenClaims.ForMember(identity, member, await signIn.UserManager.GetSecurityStampAsync(member), request.GetScopes(), [TokenClaims.Password]);
        return Results.SignIn(new ClaimsPrincipal(identity), properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    public static async Task<IResult> UserInfoAsync(HttpContext context, ClaimsPrincipal token)
    {
        var members = context.RequestServices.GetRequiredService<MemberManager>();
        var member = token.GetClaim(Claims.Subject) is { } subject ? await members.FindByIdAsync(subject) : null;
        if (member is null || member.DeactivatedAt is not null)
        {
            return Results.Challenge(
                new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidToken,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The token does not belong to a member who can sign in.",
                }),
                [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }

        var claims = new Dictionary<string, object?>(StringComparer.Ordinal) { [Claims.Subject] = member.Id.ToString("D") };
        if (token.HasScope(Scopes.Profile))
        {
            claims[Claims.Name] = member.DisplayName ?? member.Email;
        }

        if (token.HasScope(Scopes.Email))
        {
            claims[Claims.Email] = member.Email;
            claims[Claims.EmailVerified] = member.EmailConfirmed;
        }

        return Results.Ok(claims);
    }

    private static bool IsSet(ImmutableDictionary<string, JsonElement> properties, string name) =>
        properties.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.True;
}

// Takes the direct login grant out of the discovery document while cms.Members.AllowDirectLogin is off, so that the
// document names it only while the token endpoint takes it.
internal sealed class MemberGrantDiscoveryHandler(MemberOptions options) : IOpenIddictServerHandler<HandleConfigurationRequestContext>
{
    public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
        OpenIddictServerHandlerDescriptor.CreateBuilder<HandleConfigurationRequestContext>()
            .UseSingletonHandler<MemberGrantDiscoveryHandler>()
            .SetOrder(OpenIddictServerHandlers.Discovery.AttachGrantTypes.Descriptor.Order + 500)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public ValueTask HandleAsync(HandleConfigurationRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!options.AllowDirectLogin)
        {
            context.GrantTypes.Remove(AuthorizationServer.MemberPasswordGrant);
        }

        return ValueTask.CompletedTask;
    }
}
