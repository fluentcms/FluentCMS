using System.Collections.Immutable;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using FluentCMS.Agents;
using FluentCMS.Core.Identity.Members;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace FluentCMS.Core.Identity.Oidc;

// The endpoints of the authorization server. OpenIddict reads and checks every request while the request authenticates and
// answers the bad ones itself; what it passes through arrives here. It answers introspection, revocation, discovery and the
// key set without passing them through, but they are routed all the same, so that they get the group's CORS and headers.
internal static class ConnectEndpoints
{
    // The errors the error page shows for a client that registered itself and is not approved.
    public const string ClientPendingApproval = "client_pending_approval";
    public const string ClientRejected = "client_rejected";

    public static void Map(RouteGroupBuilder connect)
    {
        connect.MapMethods("authorize", [HttpMethods.Get, HttpMethods.Post], (Delegate)AuthorizeAsync).ExcludeFromDescription();
        connect.MapPost("token", (Delegate)TokenAsync).ExcludeFromDescription();
        connect.MapMethods("userinfo", [HttpMethods.Get, HttpMethods.Post], (Delegate)UserInfoAsync).ExcludeFromDescription();
        connect.MapMethods("logout", [HttpMethods.Get, HttpMethods.Post], (Delegate)LogoutAsync).ExcludeFromDescription();
        connect.MapPost("introspect", AnsweredByServer).ExcludeFromDescription();
        connect.MapPost("revoke", AnsweredByServer).ExcludeFromDescription();
        connect.MapPost("register", (Delegate)DynamicClientRegistration.RegisterAsync).ExcludeFromDescription();
    }

    public static void MapWellKnown(RouteGroupBuilder wellKnown)
    {
        wellKnown.MapGet("openid-configuration", AnsweredByServer).ExcludeFromDescription();
        wellKnown.MapGet("jwks", AnsweredByServer).ExcludeFromDescription();
    }

    private static IResult AnsweredByServer() => Results.NotFound();

    // The user signs in on the hosted pages, which hold the Identity cookie; without it the request goes there and comes back.
    // So does a user whose sign-in is older than the client or the server accepts, one whose role requires a second factor
    // the sign-in did not give, one who must choose a new password, and one who has not allowed a third-party client what it
    // asks.
    private static async Task<IResult> AuthorizeAsync(HttpContext context)
    {
        if (context.GetOpenIddictServerResponse() is { Error: not null } failed)
        {
            return ErrorPage(context, failed);
        }

        var request = ServerRequest(context);
        var applications = context.RequestServices.GetRequiredService<IOpenIddictApplicationManager>();
        var application = await applications.FindByClientIdAsync(request.ClientId!, context.RequestAborted)
            ?? throw new InvalidOperationException("The authorization server passed a client it did not find.");

        // A client that registered itself gets nothing until an administrator has approved it, and the user is not asked
        // to sign in for it. Its redirect URI is as unvetted as the client, so the refusal is shown here.
        var properties = await applications.GetPropertiesAsync(application, context.RequestAborted);
        var registered = McpClientState.Of(properties);
        if (registered is { IsApproved: false })
        {
            return ErrorPage(context, registered.Status == McpClientStatus.Rejected
                ? new OpenIddictResponse { Error = ClientRejected, ErrorDescription = "An administrator rejected the client." }
                : new OpenIddictResponse { Error = ClientPendingApproval, ErrorDescription = "The client waits for an administrator to approve it." });
        }

        // The client names its pool: one of the member pool is answered from the member cookie and never from the admin's.
        if (MemberConnect.IsMemberClient(properties))
        {
            return await MemberConnect.AuthorizeAsync(context, request);
        }

        var cookie = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var signIn = context.RequestServices.GetRequiredService<SignInManager<CmsUser>>();
        var user = cookie.Succeeded ? await signIn.ValidateSecurityStampAsync(cookie.Principal) : null;
        var parameters = context.Request.HasFormContentType ? context.Request.Form.ToList() : context.Request.Query.ToList();
        var returnUrl = context.Request.PathBase + context.Request.Path + QueryString.Create(parameters);
        var afterSignIn = context.Request.PathBase + context.Request.Path + QueryString.Create(WithoutReauthentication(parameters));

        if (user is null || !await MaySignInAsync(signIn, user))
        {
            if (request.HasPromptValue(PromptValues.None))
            {
                return Refuse(Errors.LoginRequired, "The user is not signed in.");
            }

            return Results.Challenge(new AuthenticationProperties { RedirectUri = afterSignIn }, [IdentityConstants.ApplicationScheme]);
        }

        // prompt=login, a max_age the sign-in is older than, or a sign-in older than the absolute session lifetime: the user
        // signs in again over the cookie, as for the second factor below.
        var now = context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        var signedInAt = CmsSignInManager.SignedInAt(cookie.Properties!) ?? now;
        if (MustSignInAgain(context, request, signedInAt, now))
        {
            return request.HasPromptValue(PromptValues.None)
                ? Refuse(Errors.LoginRequired, "The sign-in is older than the client or the server accepts.")
                : Reauthentication.Challenge(context, request, parameters, signedInAt, now, IdentityConstants.ApplicationScheme);
        }

        // The cookie says how the user signed in: amr=mfa after the second factor, pwd after a password alone, which is also
        // what a remembered device gives. A role that requires two-factor takes mfa only, so a user who has the second factor
        // signs in again (the sign-in manager then asks for it whatever the device), and one who has none sets it up first.
        var methods = cookie.Principal!.FindAll(Claims.AuthenticationMethodReference).Select(claim => claim.Value).ToList();
        var twoFactor = context.RequestServices.GetRequiredService<TwoFactorService>();
        if (!methods.Contains(TokenClaims.MultiFactor) && await twoFactor.IsRequiredAsync(user.Id, context.RequestAborted))
        {
            if (await twoFactor.IsOnAsync(user))
            {
                return request.HasPromptValue(PromptValues.None)
                    ? Refuse(Errors.LoginRequired, "The user's role requires a sign-in with a second factor.")
                    : Reauthentication.Challenge(context, request, parameters, signedInAt, now, IdentityConstants.ApplicationScheme);
            }

            return request.HasPromptValue(PromptValues.None)
                ? Refuse(Errors.InteractionRequired, "The user's role requires two-factor sign-in, which the user has not set up.")
                : Results.Redirect(context.Request.PathBase + HostedPagePaths.EnableTwoFactor + QueryString.Create("ReturnUrl", returnUrl));
        }

        if (user.MustChangePassword)
        {
            return request.HasPromptValue(PromptValues.None)
                ? Refuse(Errors.InteractionRequired, "The user must choose a new password.")
                : Results.Redirect(context.Request.PathBase + HostedPagePaths.ChangePassword + QueryString.Create("ReturnUrl", returnUrl));
        }

        // The session starts at the sign-in, not at this request, so that every authorization issued under it ends with it.
        var identity = TokenClaims.Identity()
            .SetClaim(Claims.AuthenticationTime, signedInAt.ToUnixTimeSeconds())
            .SetClaim(TokenClaims.SessionStart, signedInAt.ToUnixTimeSeconds());
        TokenClaims.ForUser(identity, user, await signIn.UserManager.GetSecurityStampAsync(user), request.GetScopes(), registered?.RoleId, methods);
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
        else if (CmsSignInManager.ServerSession(cookie.Properties!) is { } serverSession)
        {
            TokenClaims.ForServerSession(identity, serverSession);
            identity.SetAuthorizationId(await context.RequestServices.GetRequiredService<UserSessions>()
                .BeginAsync(new ClaimsPrincipal(identity), application, serverSession, context.RequestAborted));
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
            if (MemberConnect.IsMemberToken(result.Principal))
            {
                return await MemberConnect.RedeemAsync(context, request, result.Principal!);
            }

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

            // A session from before the user's role required two-factor ends when it is refreshed.
            if (!result.Principal!.HasClaim(Claims.AuthenticationMethodReference, TokenClaims.MultiFactor)
                && await context.RequestServices.GetRequiredService<TwoFactorService>().IsRequiredAsync(user.Id, context.RequestAborted))
            {
                return Refuse(Errors.InvalidGrant, "The user's role requires a sign-in with a second factor.");
            }

            // Rolling refresh tokens would keep a session for ever; the absolute lifetime ends it however often it is refreshed.
            // A session from before the start was kept counts from its first refresh since.
            var identity = TokenClaims.Identity(result.Principal);
            var now = context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
            if (!long.TryParse(identity.GetClaim(TokenClaims.SessionStart), NumberStyles.None, CultureInfo.InvariantCulture, out var started))
            {
                identity.SetClaim(TokenClaims.SessionStart, started = now.ToUnixTimeSeconds());
            }

            if (context.RequestServices.GetRequiredService<ClientOptions>().AbsoluteSessionLifetime is { } cap
                && now - DateTimeOffset.FromUnixTimeSeconds(started) > cap)
            {
                return Refuse(Errors.InvalidGrant, "The session has reached its absolute lifetime; the user signs in again.");
            }

            // The client is read again, so a token of a client that registered itself carries the role the client has now,
            // and one that is not approved, which no code was issued to, redeems nothing either.
            var clients = context.RequestServices.GetRequiredService<IOpenIddictApplicationManager>();
            var client = (request.ClientId ?? result.Principal!.GetPresenters().FirstOrDefault()) is { } clientId
                ? await clients.FindByClientIdAsync(clientId, context.RequestAborted)
                : null;
            if (client is null)
            {
                return Refuse(Errors.InvalidClient, "The client is not known.");
            }

            var properties = await clients.GetPropertiesAsync(client, context.RequestAborted);
            var registered = McpClientState.Of(properties);
            if (registered is { IsApproved: false })
            {
                return Refuse(Errors.UnauthorizedClient, "The client is not approved.");
            }

            // A client that the configuration moved to the member pool redeems no admin user's token.
            if (MemberConnect.IsMemberClient(properties))
            {
                return Refuse(Errors.InvalidGrant, "The token was issued to a client of the admin pool, which this client is not.");
            }

            TokenClaims.ForUser(identity, user, stamp, result.Principal!.GetScopes(), registered?.RoleId);
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
            if (MemberConnect.IsMemberClient(properties))
            {
                return Refuse(Errors.UnauthorizedClient, "A client of the member pool is not a service account.");
            }

            var roles = await RoleIdsAsync(context.RequestServices, properties, context.RequestAborted);

            var identity = TokenClaims.ForServiceAccount(TokenClaims.Identity(), id, name, roles, request.GetScopes());
            return Results.SignIn(new ClaimsPrincipal(identity), properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (request.GrantType == AuthorizationServer.MemberPasswordGrant)
        {
            return await MemberConnect.DirectLoginAsync(context, request);
        }

        return Refuse(Errors.UnsupportedGrantType, "The grant type is not supported.");
    }

    private static async Task<IResult> UserInfoAsync(HttpContext context)
    {
        var result = await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        if (MemberConnect.IsMemberToken(result.Principal))
        {
            return await MemberConnect.UserInfoAsync(context, result.Principal!);
        }

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
    // or to the logout page. The user confirms on the logout page, whose form posts back with an antiforgery token, or the
    // client sends a valid id_token_hint issued in the signed-in user's server session. Anything else, a cross-site POST
    // included, goes to the logout page, which asks. A request without the sign-in cookie has no sign-in to end, so it
    // sends no expiring cookie either.
    // Ending a sign-in revokes the authorizations issued in its server session, with their tokens, and no others.
    // The client the request names, by its client_id or by the audience of its hint, says whose sign-in ends: a client of
    // the member pool ends the member's on the member pages, and any other request the admin user's, so that neither
    // signs the other out.
    private static async Task<IResult> LogoutAsync(HttpContext context)
    {
        if (context.GetOpenIddictServerResponse() is { Error: not null } failed)
        {
            return ErrorPage(context, failed);
        }

        var hint = await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var members = await IsMemberClientAsync(context, context.GetOpenIddictServerRequest()?.ClientId ?? hint.Principal?.GetAudiences().FirstOrDefault());
        var (scheme, page) = members
            ? (MemberAuthentication.CookieScheme, HostedPagePaths.MemberLogout)
            : (IdentityConstants.ApplicationScheme, HostedPagePaths.Logout);

        var cookie = await context.AuthenticateAsync(scheme);
        if (!await ConfirmedAsync(context, members) && !await HintsSignedInUserAsync(context, hint, cookie, members))
        {
            return Results.Redirect(context.Request.PathBase + page + QueryString.Create(LogoutParameters(context)));
        }

        if (cookie.Succeeded)
        {
            await EndSignInAsync(context, cookie, members);
        }

        return Results.SignOut(
            new AuthenticationProperties { RedirectUri = context.Request.PathBase + page },
            [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
    }

    private static async Task<bool> IsMemberClientAsync(HttpContext context, string? clientId)
    {
        if (string.IsNullOrEmpty(clientId))
        {
            return false;
        }

        var applications = context.RequestServices.GetRequiredService<IOpenIddictApplicationManager>();
        return await applications.FindByClientIdAsync(clientId, context.RequestAborted) is { } application
            && MemberConnect.IsMemberClient(await applications.GetPropertiesAsync(application, context.RequestAborted));
    }

    // The form of the logout page, with the antiforgery token the page rendered. The two logout pages share that token,
    // so the form also says which page it is: the member page marks its form, and only the page of the pool that is signed
    // out confirms it.
    private static async Task<bool> ConfirmedAsync(HttpContext context, bool members) =>
        HttpMethods.IsPost(context.Request.Method) && context.Request.HasFormContentType
        && (context.Request.Form[MemberAuthentication.LogoutPageField] == MemberAuthentication.LogoutPageMark) == members
        && await context.RequestServices.GetRequiredService<IAntiforgery>().IsRequestValidAsync(context);

    // What the logout page posts back: the client's request, without a token or a page's mark of an earlier form.
    private static IEnumerable<KeyValuePair<string, StringValues>> LogoutParameters(HttpContext context)
    {
        var token = context.RequestServices.GetRequiredService<IOptions<AntiforgeryOptions>>().Value.FormFieldName;
        IEnumerable<KeyValuePair<string, StringValues>> parameters = context.Request.HasFormContentType ? context.Request.Form : context.Request.Query;
        return parameters.Where(parameter =>
            !string.Equals(parameter.Key, token, StringComparison.Ordinal)
            && !string.Equals(parameter.Key, MemberAuthentication.LogoutPageField, StringComparison.OrdinalIgnoreCase));
    }

    // An error the server could not return to the client, such as an unknown client or redirect URI. The error page shows
    // the code; the description stays in the query for whoever debugs the client.
    private static IResult ErrorPage(HttpContext context, OpenIddictResponse response) =>
        Results.Redirect(context.Request.PathBase + HostedPagePaths.Error + QueryString.Create(new Dictionary<string, string?>
        {
            [Parameters.Error] = response.Error,
            [Parameters.ErrorDescription] = response.ErrorDescription,
        }));

    // OpenIddict passes a hint it cannot read through without a principal, so the hint is checked here. Without the cookie
    // a GET, which a browser sends with it, has no one signed in, so an identity token of this server for the client the
    // request names returns to it, an expired one or a repeated logout's, whose token the first one revoked, included; any
    // other hint goes to the logout page. A POST may be a cross-site one, which never carries it.
    // With the cookie, the hint must be of the cookie's server session (its sid), not only of its user: an id token from
    // another session or device, or from an earlier sign-in, goes to the logout page. The cookie is the one of the pool the
    // request is for, read by that pool's manager.
    private static async Task<bool> HintsSignedInUserAsync(HttpContext context, AuthenticateResult hint, AuthenticateResult cookie, bool members)
    {
        if (!cookie.Succeeded)
        {
            return HttpMethods.IsGet(context.Request.Method) && await IsOwnIdentityTokenAsync(context, ServerRequest(context).IdTokenHint);
        }

        return hint.Principal?.GetClaim(Claims.Subject) is { } subject
            && SignedInUserId(context, cookie, members) == subject
            && CmsSignInManager.ServerSession(cookie.Properties!) is { } serverSession
            && hint.Principal.GetClaim(TokenClaims.ServerSession) == serverSession;
    }

    // An identity token signed with a key of this server, whatever its lifetime or its entry's status, of the client the
    // request names and whose post-logout URI it asks for. OpenIddict checks those only on a hint it can read.
    private static async Task<bool> IsOwnIdentityTokenAsync(HttpContext context, string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        var options = context.RequestServices.GetRequiredService<IOptionsMonitor<OpenIddictServerOptions>>().CurrentValue;
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            IssuerSigningKeys = [.. options.SigningCredentials.Select(credentials => credentials.Key)],
            ValidAlgorithms = [.. options.SigningCredentials.Select(credentials => credentials.Algorithm).Distinct(StringComparer.Ordinal)],
            ValidTypes = [JwtConstants.HeaderType],
            ValidateAudience = false,
            ValidateIssuer = false,
            ValidateLifetime = false,
        });
        if (!result.IsValid || result.SecurityToken is not JsonWebToken identity)
        {
            return false;
        }

        var audiences = identity.Audiences.ToList();
        var clientId = identity.TryGetPayloadValue<string>(Claims.AuthorizedParty, out var party) ? party : audiences.Count == 1 ? audiences[0] : null;
        var request = ServerRequest(context);
        if (clientId is null || (request.ClientId is { } named && named != clientId))
        {
            return false;
        }

        if (string.IsNullOrEmpty(request.PostLogoutRedirectUri))
        {
            return true;
        }

        var applications = context.RequestServices.GetRequiredService<IOpenIddictApplicationManager>();
        return await applications.FindByClientIdAsync(clientId, context.RequestAborted) is { } application
            && await applications.ValidatePostLogoutRedirectUriAsync(application, request.PostLogoutRedirectUri, context.RequestAborted);
    }

    // Ends the cookie's sign-in and, with it, the authorizations of its server session, so that nothing issued in a
    // session outlives its cookie; an end the browser stops waiting for still ends them.
    private static async Task EndSignInAsync(HttpContext context, AuthenticateResult cookie, bool members)
    {
        await context.RequestServices.GetRequiredService<UserSessions>()
            .EndAsync(SignedInUserId(context, cookie, members), CmsSignInManager.ServerSession(cookie.Properties!), CancellationToken.None);
        await context.SignOutAsync(members ? MemberAuthentication.CookieScheme : IdentityConstants.ApplicationScheme);
    }

    private static string? SignedInUserId(HttpContext context, AuthenticateResult cookie, bool members) => members
        ? context.RequestServices.GetRequiredService<MemberManager>().GetUserId(cookie.Principal!)
        : context.RequestServices.GetRequiredService<UserManager<CmsUser>>().GetUserId(cookie.Principal!);

    // prompt=login or a max_age the sign-in is older than, unless a sign-in since the request that asked answered it, or a
    // sign-in older than the absolute session lifetime.
    internal static bool MustSignInAgain(HttpContext context, OpenIddictRequest request, DateTimeOffset signedInAt, DateTimeOffset now)
    {
        var asked = request.HasPromptValue(PromptValues.Login) || (request.MaxAge is { } maxAge && now - signedInAt > TimeSpan.FromSeconds(maxAge));
        return (asked && !Reauthentication.IsAnswered(context, request, signedInAt, now))
            || (context.RequestServices.GetRequiredService<ClientOptions>().AbsoluteSessionLifetime is { } cap && now - signedInAt > cap);
    }

    // Without a sign-in, the one the user makes answers prompt=login and max_age, so the request it comes back with no
    // longer asks for one.
    internal static IEnumerable<KeyValuePair<string, StringValues>> WithoutReauthentication(List<KeyValuePair<string, StringValues>> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            if (name == Parameters.Prompt)
            {
                var prompts = value.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(prompt => prompt != PromptValues.Login).ToArray();
                if (prompts.Length > 0)
                {
                    yield return KeyValuePair.Create(name, new StringValues(string.Join(' ', prompts)));
                }
            }
            else if (name != Parameters.MaxAge)
            {
                yield return KeyValuePair.Create(name, value);
            }
        }
    }

    // Active and not locked out; CanSignInAsync does not look at lockout, which the password check alone applies.
    private static async Task<bool> MaySignInAsync(SignInManager<CmsUser> signIn, CmsUser user) =>
        await signIn.CanSignInAsync(user) && !await signIn.UserManager.IsLockedOutAsync(user);

    private static OpenIddictRequest ServerRequest(HttpContext context) =>
        context.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("The request did not pass through the authorization server.");

    internal static IResult Refuse(string error, string description) => Results.Forbid(
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
