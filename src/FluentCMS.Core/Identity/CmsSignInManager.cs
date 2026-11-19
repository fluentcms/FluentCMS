using System.Globalization;
using System.Security.Claims;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Core.Identity.Sso;
using FluentCMS.Events;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace FluentCMS.Core.Identity;

// Every way in goes through CanSignInAsync, so a deactivated user is refused whatever its password, lockout or provider.
// Each step of a sign-in raises LoginSucceededEvent or LoginFailedEvent here, so a host's own pages raise them too; a
// password that leaves the second factor to come raises nothing yet. With cms.Sso.DisableLocalLogin no password signs in.
internal sealed class CmsSignInManager(
    UserManager<CmsUser> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<CmsUser> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<CmsUser>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<CmsUser> confirmation,
    SignInEvents events,
    TwoFactorService twoFactor,
    SsoOptions sso,
    TimeProvider time)
    : SignInManager<CmsUser>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    // When the user signed in, in the cookie's properties, which its renewals and the stamp check keep while they move its
    // issue time: prompt=login, max_age and the absolute session lifetime read it. A cookie without it is no sign-in for
    // them, since its issue time is all it has and every renewal moves that.
    internal const string SignedInAtItem = ".fcms.signed_in_at";

    // The id of the server session, kept in the cookie's properties in the same way: the authorizations issued in it carry
    // it, so that the logout of that session ends them (#3198). A cookie from before has none.
    internal const string ServerSessionItem = ".fcms.server_session";

    public static DateTimeOffset? SignedInAt(AuthenticationProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        return properties.Items.TryGetValue(SignedInAtItem, out var value) && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
    }

    // The time of a cookie's sign-in while the authorize endpoints and the hosted pages that need a signed-in user take it,
    // in both pools: the cookie says when its sign-in was, and that is within AbsoluteSessionLifetime. Otherwise the user
    // signs in again over the cookie, which writes a new time; the logout pages take any cookie.
    internal static DateTimeOffset? LiveSignIn(HttpContext context, AuthenticationProperties properties)
    {
        ArgumentNullException.ThrowIfNull(context);

        var now = context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        return SignedInAt(properties) is { } signedInAt && !context.RequestServices.GetRequiredService<ClientOptions>().HasOutlived(signedInAt, now)
            ? signedInAt
            : null;
    }

    public static string? ServerSession(AuthenticationProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        return properties.Items.TryGetValue(ServerSessionItem, out var value) && !string.IsNullOrEmpty(value) ? value : null;
    }

    // Marks a new sign-in with its time and the id of its server session, unless the properties are a renewal's.
    internal static void MarkSignIn(AuthenticationProperties properties, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(time);

        properties.Items.TryAdd(SignedInAtItem, time.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        properties.Items.TryAdd(ServerSessionItem, Guid.NewGuid().ToString("N"));
    }

    // A new sign-in over a live cookie of the pool: the same user's keeps the browser's server session, so that its logout
    // still finds what was issued before; another user's ends that session, as a logout would.
    internal static async Task TakeOverAsync(HttpContext context, string scheme, string userId, AuthenticationProperties properties, Func<ClaimsPrincipal, string?> userIdOf)
    {
        if (properties.Items.ContainsKey(ServerSessionItem))
        {
            return;
        }

        var live = await context.AuthenticateAsync(scheme);
        if (!live.Succeeded || ServerSession(live.Properties!) is not { } serverSession)
        {
            return;
        }

        var previous = userIdOf(live.Principal!);
        if (previous == userId)
        {
            properties.Items[ServerSessionItem] = serverSession;
        }
        else
        {
            await context.RequestServices.GetRequiredService<UserSessions>().EndAsync(previous, serverSession, CancellationToken.None);
        }
    }

    public override async Task<bool> CanSignInAsync(CmsUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return user.DeactivatedAt is null && await base.CanSignInAsync(user);
    }

    // A remembered device skips the second factor and signs in with amr=pwd, which a role that requires two-factor does not
    // take; a user who holds one gives the second factor at every sign-in.
    public override async Task<bool> IsTwoFactorClientRememberedAsync(CmsUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return !await twoFactor.IsRequiredAsync(user.Id, Context.RequestAborted) && await base.IsTwoFactorClientRememberedAsync(user);
    }

    // The name is a user name or, failing that, an email address.
    public override async Task<SignInResult> PasswordSignInAsync(string userName, string password, bool isPersistent, bool lockoutOnFailure)
    {
        ArgumentNullException.ThrowIfNull(userName);
        ArgumentNullException.ThrowIfNull(password);

        if (sso.DisableLocalLogin)
        {
            await events.FailedAsync(Context, null, LoginMethods.Password, LoginFailureReasons.NotAllowed);
            return SignInResult.NotAllowed;
        }

        var user = await UserManager.FindByNameAsync(userName)
            ?? (userName.Contains('@', StringComparison.Ordinal) ? await UserManager.FindByEmailAsync(userName) : null);
        if (user is null)
        {
            // As long as a password check, so that the time of the answer does not tell whether the name exists.
            _ = UserManager.PasswordHasher.HashPassword(new CmsUser(), password);
            await events.FailedAsync(Context, null, LoginMethods.Password, LoginFailureReasons.UnknownUser);
            return SignInResult.Failed;
        }

        return await PasswordSignInAsync(user, password, isPersistent, lockoutOnFailure);
    }

    public override async Task<SignInResult> PasswordSignInAsync(CmsUser user, string password, bool isPersistent, bool lockoutOnFailure)
    {
        var result = await base.PasswordSignInAsync(user, password, isPersistent, lockoutOnFailure);
        await RecordAsync(user, LoginMethods.Password, result);
        return result;
    }

    // A user who may not sign in, deactivated or locked out, is refused before Identity checks the password, so the hash
    // runs anyway: the answer takes as long as for a wrong password or an unknown name.
    public override async Task<SignInResult> CheckPasswordSignInAsync(CmsUser user, string password, bool lockoutOnFailure)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(password);

        if (sso.DisableLocalLogin)
        {
            return SignInResult.NotAllowed;
        }

        if (await PreSignInCheck(user) is { } refused)
        {
            _ = UserManager.PasswordHasher.HashPassword(user, password);
            return refused;
        }

        return await base.CheckPasswordSignInAsync(user, password, lockoutOnFailure);
    }

    // Every sign-in that makes a new cookie comes here; a renewal passes the cookie's properties, which keep the time. One
    // with the second factor ends what its server session was issued without it.
    public override async Task SignInWithClaimsAsync(CmsUser user, AuthenticationProperties? authenticationProperties, IEnumerable<Claim> additionalClaims)
    {
        ArgumentNullException.ThrowIfNull(user);

        List<Claim> claims = [.. additionalClaims ?? []];
        var properties = authenticationProperties ?? new AuthenticationProperties();
        await TakeOverAsync(Context, IdentityConstants.ApplicationScheme, user.Id.ToString("D"), properties, UserManager.GetUserId);
        if (properties.IssuedUtc is null && !properties.Items.ContainsKey(SignedInAtItem))
        {
            await KeepSsoSessionAsync(user, properties, claims);
        }

        MarkSignIn(properties, time);
        if (claims.Exists(claim => claim.Type == Claims.AuthenticationMethodReference && claim.Value == TokenClaims.MultiFactor))
        {
            await Context.RequestServices.GetRequiredService<UserSessions>().EndSingleFactorAsync(user.Id.ToString("D"), ServerSession(properties), CancellationToken.None);
        }

        await base.SignInWithClaimsAsync(user, properties, claims);
    }

    // Identity's refresh keeps the first amr only, which after the enable-two-factor page is pwd and drops mfa; every one is
    // kept here, with the provider.
    public override async Task RefreshSignInAsync(CmsUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var auth = await Context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (!auth.Succeeded || auth.Principal is null)
        {
            return;
        }

        if (UserManager.GetUserId(auth.Principal) != await UserManager.GetUserIdAsync(user))
        {
            throw new InvalidOperationException("The cookie belongs to another user, so it cannot be renewed for this one.");
        }

        List<Claim> methods = [.. auth.Principal.FindAll(claim => claim.Type is Claims.AuthenticationMethodReference or ClaimTypes.AuthenticationMethod)
            .Select(claim => new Claim(claim.Type, claim.Value))];
        await SignInWithClaimsAsync(user, auth.Properties, methods);
    }

    public override async Task<SignInResult> TwoFactorAuthenticatorSignInAsync(string code, bool isPersistent, bool rememberClient)
    {
        var user = await GetTwoFactorAuthenticationUserAsync();
        var result = await base.TwoFactorAuthenticatorSignInAsync(code, isPersistent, rememberClient);
        await RecordAsync(user, LoginMethods.TwoFactor, result);
        return result;
    }

    public override async Task<SignInResult> TwoFactorSignInAsync(string provider, string code, bool isPersistent, bool rememberClient)
    {
        var user = await GetTwoFactorAuthenticationUserAsync();
        var result = await base.TwoFactorSignInAsync(provider, code, isPersistent, rememberClient);
        await RecordAsync(user, LoginMethods.TwoFactor, result);
        return result;
    }

    // Identity's recovery code sign-in neither looks at lockout nor counts a wrong code, so both are done here, as the
    // authenticator's sign-in and TwoFactorService do: a wrong code counts towards the lockout of cms.Users.
    public override async Task<SignInResult> TwoFactorRecoveryCodeSignInAsync(string recoveryCode)
    {
        var user = await GetTwoFactorAuthenticationUserAsync();
        if (user is null)
        {
            return SignInResult.Failed;
        }

        var result = await PreSignInCheck(user) ?? await base.TwoFactorRecoveryCodeSignInAsync(recoveryCode);
        if (result is { Succeeded: false, IsLockedOut: false, IsNotAllowed: false, RequiresTwoFactor: false } && UserManager.SupportsUserLockout)
        {
            await UserManager.AccessFailedAsync(user);
            if (await UserManager.IsLockedOutAsync(user))
            {
                result = SignInResult.LockedOut;
            }
        }

        await RecordAsync(user, LoginMethods.RecoveryCode, result);
        return result;
    }

    public override async Task<SignInResult> ExternalLoginSignInAsync(string loginProvider, string providerKey, bool isPersistent, bool bypassTwoFactor)
    {
        var user = await UserManager.FindByLoginAsync(loginProvider, providerKey);
        var result = await base.ExternalLoginSignInAsync(loginProvider, providerKey, isPersistent, bypassTwoFactor);
        if (user is null)
        {
            await events.FailedAsync(Context, null, LoginMethods.External, LoginFailureReasons.UnknownUser);
        }
        else
        {
            await RecordAsync(user, LoginMethods.External, result);
        }

        return result;
    }

    // A provider trusted for its own second factor that asserted one signs the user in with amr=mfa, as the authenticator
    // does, and the local second factor is not asked for.
    public async Task<SignInResult> ExternalSignInAsync(string loginProvider, string providerKey, bool multiFactor)
    {
        if (!multiFactor)
        {
            return await ExternalLoginSignInAsync(loginProvider, providerKey, isPersistent: false, bypassTwoFactor: false);
        }

        var user = await UserManager.FindByLoginAsync(loginProvider, providerKey);
        if (user is null)
        {
            await events.FailedAsync(Context, null, LoginMethods.External, LoginFailureReasons.UnknownUser);
            return SignInResult.Failed;
        }

        var result = await PreSignInCheck(user);
        if (result is null)
        {
            await Context.SignOutAsync(IdentityConstants.ExternalScheme);
            await SignInWithClaimsAsync(user, isPersistent: false, [new Claim(ClaimTypes.AuthenticationMethod, loginProvider), new Claim(Claims.AuthenticationMethodReference, TokenClaims.MultiFactor)]);
            result = SignInResult.Success;
        }

        await RecordAsync(user, LoginMethods.External, result);
        return result;
    }

    // A new sign-in through a provider of cms.Sso keeps the provider's session from the external cookie it was made with,
    // read as the request brought it, since Identity has already ended that cookie in the response by now. The cookie must
    // be the user's own login of the provider: at the second factor the browser may hold another sign-in's by then.
    private async Task KeepSsoSessionAsync(CmsUser user, AuthenticationProperties properties, IEnumerable<Claim> additionalClaims)
    {
        var provider = additionalClaims.FirstOrDefault(claim => claim.Type == ClaimTypes.AuthenticationMethod)?.Value;
        if (provider is null || !sso.Providers.ContainsKey(provider))
        {
            return;
        }

        var external = await Context.AuthenticateAsync(IdentityConstants.ExternalScheme);
        if (!external.Succeeded || SsoSession.Of(external.Properties!) is not { } session
            || !string.Equals(session.Provider, provider, StringComparison.OrdinalIgnoreCase)
            || external.Principal!.FindFirstValue(ClaimTypes.NameIdentifier) is not { } key)
        {
            return;
        }

        if ((await UserManager.GetLoginsAsync(user)).Any(login => string.Equals(login.LoginProvider, provider, StringComparison.OrdinalIgnoreCase) && login.ProviderKey == key))
        {
            session.WriteTo(properties);
        }
    }

    // A second factor asked for without a first one belongs to no sign-in, so it raises nothing.
    private Task RecordAsync(CmsUser? user, string method, SignInResult result)
    {
        if (user is null || result.RequiresTwoFactor)
        {
            return Task.CompletedTask;
        }

        if (result.Succeeded)
        {
            return events.SucceededAsync(Context, user, method);
        }

        var reason = result.IsLockedOut ? LoginFailureReasons.LockedOut
            : result.IsNotAllowed ? LoginFailureReasons.NotAllowed
            : LoginFailureReasons.InvalidCredentials;
        return events.FailedAsync(Context, user, method, reason);
    }
}
