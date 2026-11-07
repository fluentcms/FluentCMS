using System.Globalization;
using System.Security.Claims;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Events;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
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
    // issue time: prompt=login, max_age and the absolute session lifetime read it.
    internal const string SignedInAtItem = ".fcms.signed_in_at";

    public static DateTimeOffset? SignedInAt(AuthenticationProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        return properties.Items.TryGetValue(SignedInAtItem, out var value) && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : properties.IssuedUtc;
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

    // Every sign-in that makes a new cookie comes here; a renewal passes the cookie's properties, which keep the time.
    public override Task SignInWithClaimsAsync(CmsUser user, AuthenticationProperties? authenticationProperties, IEnumerable<Claim> additionalClaims)
    {
        var properties = authenticationProperties ?? new AuthenticationProperties();
        if (!properties.Items.ContainsKey(SignedInAtItem))
        {
            properties.Items[SignedInAtItem] = time.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        }

        return base.SignInWithClaimsAsync(user, properties, additionalClaims);
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
