using System.Security.Claims;
using System.Collections.Immutable;
using System.Text.Json;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace FluentCMS.Core.Identity.Oidc;

// The endpoints of the authorization server. OpenIddict reads and checks every request while the request authenticates and
// answers the bad ones itself; what it passes through arrives here. It answers introspection, revocation, discovery and the
// key set without passing them through, but they are routed all the same, so that they get the group's CORS and headers.
internal static class ConnectEndpoints
{
    public static void Map(RouteGroupBuilder connect)
    {
        connect.MapMethods("authorize", [HttpMethods.Get, HttpMethods.Post], (Delegate)AuthorizeAsync).ExcludeFromDescription();
        connect.MapPost("token", (Delegate)TokenAsync).ExcludeFromDescription();
        connect.MapMethods("userinfo", [HttpMethods.Get, HttpMethods.Post], (Delegate)UserInfoAsync).ExcludeFromDescription();
        connect.MapMethods("logout", [HttpMethods.Get, HttpMethods.Post], (Delegate)LogoutAsync).ExcludeFromDescription();
        connect.MapPost("introspect", AnsweredByServer).ExcludeFromDescription();
        connect.MapPost("revoke", AnsweredByServer).ExcludeFromDescription();
    }

    public static void MapWellKnown(RouteGroupBuilder wellKnown)
    {
        wellKnown.MapGet("openid-configuration", AnsweredByServer).ExcludeFromDescription();
        wellKnown.MapGet("jwks", AnsweredByServer).ExcludeFromDescription();
    }

    private static IResult AnsweredByServer() => Results.NotFound();

    // The user signs in on the hosted pages, which hold the Identity cookie; without it the request goes there and comes back.
    // So does a user who must choose a new password, and one who has not allowed a third-party client what it asks.
    private static async Task<IResult> AuthorizeAsync(HttpContext context)
    {
        if (context.GetOpenIddictServerResponse() is { Error: not null } failed)
        {
            return ErrorPage(context, failed);
        }

        var request = ServerRequest(context);
        var cookie = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var signIn = context.RequestServices.GetRequiredService<SignInManager<CmsUser>>();
        var user = cookie.Succeeded ? await signIn.ValidateSecurityStampAsync(cookie.Principal) : null;
        var parameters = context.Request.HasFormContentType ? context.Request.Form.ToList() : context.Request.Query.ToList();
        var returnUrl = context.Request.PathBase + context.Request.Path + QueryString.Create(parameters);

        if (user is null || !await MaySignInAsync(signIn, user))
        {
            if (request.HasPromptValue(PromptValues.None))
            {
                return Refuse(Errors.LoginRequired, "The user is not signed in.");
            }

            return Results.Challenge(new AuthenticationProperties { RedirectUri = returnUrl }, [IdentityConstants.ApplicationScheme]);
        }

        if (user.MustChangePassword)
        {
            return request.HasPromptValue(PromptValues.None)
                ? Refuse(Errors.InteractionRequired, "The user must choose a new password.")
                : Results.Redirect(context.Request.PathBase + HostedPagePaths.ChangePassword + QueryString.Create("ReturnUrl", returnUrl));
        }

        var identity = TokenClaims.ForUser(TokenClaims.Identity(), user, await signIn.UserManager.GetSecurityStampAsync(user), request.GetScopes());
        var applications = context.RequestServices.GetRequiredService<IOpenIddictApplicationManager>();
        var application = await applications.FindByClientIdAsync(request.ClientId!, context.RequestAborted)
            ?? throw new InvalidOperationException("The authorization server passed a client it did not find.");
        if (await applications.HasConsentTypeAsync(application, ConsentTypes.Explicit, context.RequestAborted))
        {
            var consents = context.RequestServices.GetRequiredService<ClientConsents>();
            var consent = request.HasPromptValue(PromptValues.Consent)
                ? null
                : await consents.FindAsync(user.Id, await applications.GetIdAsync(application, context.RequestAborted) ?? "", request.GetScopes(), context.RequestAborted);
            if (consent is null)
            {
                if (request.HasPromptValue(PromptValues.None))
                {
                    return Refuse(Errors.ConsentRequired, "The user has not allowed the client what it asks for.");
                }

                if ((string?)request.GetParameter(ClientConsents.DecisionParameter) == ClientConsents.Denied)
                {
                    return Refuse(Errors.AccessDenied, "The user did not allow the client what it asks for.");
                }

                return Results.Redirect(context.Request.PathBase + HostedPagePaths.Consent + QueryString.Create(parameters));
            }

            identity.SetAuthorizationId(await context.RequestServices.GetRequiredService<IOpenIddictAuthorizationManager>().GetIdAsync(consent, context.RequestAborted));
        }

        return Results.SignIn(new ClaimsPrincipal(identity), properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> TokenAsync(HttpContext context)
    {
        var request = ServerRequest(context);
        if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType())
        {
            // The code or refresh token keeps its scopes and session; the user is read again, so one who can no longer
            // sign in, or whose password changed since, gets no new token.
            var result = await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            var signIn = context.RequestServices.GetRequiredService<SignInManager<CmsUser>>();
            var users = signIn.UserManager;
            var user = result.Principal?.GetClaim(Claims.Subject) is { } subject ? await users.FindByIdAsync(subject) : null;
            if (user is null || !await MaySignInAsync(signIn, user))
            {
                return Refuse(Errors.InvalidGrant, "The user can no longer sign in.");
            }

            var stamp = await users.GetSecurityStampAsync(user);
            if (!string.Equals(result.Principal!.GetClaim(TokenClaims.SecurityStamp), stamp, StringComparison.Ordinal))
            {
                return Refuse(Errors.InvalidGrant, "The user's credentials changed since the token was issued.");
            }

            var identity = TokenClaims.ForUser(TokenClaims.Identity(result.Principal), user, stamp, result.Principal!.GetScopes());
            return Results.SignIn(new ClaimsPrincipal(identity), properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (request.IsClientCredentialsGrantType())
        {
            var applications = context.RequestServices.GetRequiredService<IOpenIddictApplicationManager>();
            var application = await applications.FindByClientIdAsync(request.ClientId!, context.RequestAborted)
                ?? throw new InvalidOperationException("The authorization server passed a client it did not find.");

            var id = Guid.Parse(await applications.GetIdAsync(application, context.RequestAborted) ?? "");
            var name = await applications.GetDisplayNameAsync(application, context.RequestAborted) ?? request.ClientId!;
            var properties = await applications.GetPropertiesAsync(application, context.RequestAborted);
            var roles = await RoleIdsAsync(context.RequestServices, properties, context.RequestAborted);

            var identity = TokenClaims.ForServiceAccount(TokenClaims.Identity(), id, name, roles, request.GetScopes());
            return Results.SignIn(new ClaimsPrincipal(identity), properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        return Refuse(Errors.UnsupportedGrantType, "The grant type is not supported.");
    }

    private static async Task<IResult> UserInfoAsync(HttpContext context)
    {
        var result = await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var users = context.RequestServices.GetRequiredService<UserManager<CmsUser>>();
        var user = result.Principal?.GetClaim(Claims.Subject) is { } subject ? await users.FindByIdAsync(subject) : null;
        if (user is null || user.DeactivatedAt is not null)
        {
            return Results.Challenge(
                new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidToken,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The token does not belong to a user who can sign in.",
                }),
                [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }

        var claims = new Dictionary<string, object?>(StringComparer.Ordinal) { [Claims.Subject] = user.Id.ToString("D") };
        if (result.Principal!.HasScope(Scopes.Profile))
        {
            claims[Claims.Name] = user.UserName;
        }

        if (result.Principal!.HasScope(Scopes.Email))
        {
            claims[Claims.Email] = user.Email;
            claims[Claims.EmailVerified] = user.EmailConfirmed;
        }

        return Results.Ok(claims);
    }

    // Ends the user's sign-in on the hosted pages; the server then returns to the client's post-logout URI when it gave one,
    // or to the logout page. A cross-site GET would sign anyone out, so a GET needs a valid id_token_hint of the signed-in
    // user; the cookie is SameSite=Lax, so a cross-site POST does not carry it. Any other GET goes to the logout page, which
    // asks the user and posts the request back.
    private static async Task<IResult> LogoutAsync(HttpContext context)
    {
        if (context.GetOpenIddictServerResponse() is { Error: not null } failed)
        {
            return ErrorPage(context, failed);
        }

        if (HttpMethods.IsGet(context.Request.Method) && !await HintsSignedInUserAsync(context))
        {
            return Results.Redirect(context.Request.PathBase + HostedPagePaths.Logout + context.Request.QueryString);
        }

        await context.SignOutAsync(IdentityConstants.ApplicationScheme);
        return Results.SignOut(
            new AuthenticationProperties { RedirectUri = context.Request.PathBase + HostedPagePaths.Logout },
            [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
    }

    // An error the server could not return to the client, such as an unknown client or redirect URI. The error page shows
    // the code; the description stays in the query for whoever debugs the client.
    private static IResult ErrorPage(HttpContext context, OpenIddictResponse response) =>
        Results.Redirect(context.Request.PathBase + HostedPagePaths.Error + QueryString.Create(new Dictionary<string, string?>
        {
            [Parameters.Error] = response.Error,
            [Parameters.ErrorDescription] = response.ErrorDescription,
        }));

    // OpenIddict passes a hint it cannot read through without a principal, so the hint is checked here.
    private static async Task<bool> HintsSignedInUserAsync(HttpContext context)
    {
        var hint = await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        if (hint.Principal?.GetClaim(Claims.Subject) is not { } subject)
        {
            return false;
        }

        var cookie = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        return !cookie.Succeeded
            || context.RequestServices.GetRequiredService<UserManager<CmsUser>>().GetUserId(cookie.Principal) == subject;
    }

    // Active and not locked out; CanSignInAsync does not look at lockout, which the password check alone applies.
    private static async Task<bool> MaySignInAsync(SignInManager<CmsUser> signIn, CmsUser user) =>
        await signIn.CanSignInAsync(user) && !await signIn.UserManager.IsLockedOutAsync(user);

    private static OpenIddictRequest ServerRequest(HttpContext context) =>
        context.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("The request did not pass through the authorization server.");

    private static IResult Refuse(string error, string description) => Results.Forbid(
        new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
        }),
        [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);

    // A service account holds the instance-wide roles its client names, as they are when the token is issued; a name that
    // matches no role gives nothing.
    private static async Task<IReadOnlyList<Guid>> RoleIdsAsync(IServiceProvider services, ImmutableDictionary<string, JsonElement> properties, CancellationToken cancellationToken)
    {
        if (!properties.TryGetValue(AuthorizationServer.RolesProperty, out var element) || element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<string> names = [.. element.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!)];
        var context = services.GetRequiredService<SystemDbContext>();
        return await context.Set<Role>().AsNoTracking()
            .Where(role => role.SiteId == null && names.Contains(role.Name))
            .Select(role => role.Id)
            .ToListAsync(cancellationToken);
    }
}
