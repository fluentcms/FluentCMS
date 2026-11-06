using FluentCMS.Events;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluentCMS.Core.Identity;

// Every way in goes through CanSignInAsync, so a deactivated user is refused whatever its password, lockout or provider.
// Each step of a sign-in raises LoginSucceededEvent or LoginFailedEvent here, so a host's own pages raise them too; a
// password that leaves the second factor to come raises nothing yet.
internal sealed class CmsSignInManager(
    UserManager<CmsUser> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<CmsUser> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<CmsUser>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<CmsUser> confirmation,
    SignInEvents events)
    : SignInManager<CmsUser>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    public override async Task<bool> CanSignInAsync(CmsUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return user.DeactivatedAt is null && await base.CanSignInAsync(user);
    }

    // The name is a user name or, failing that, an email address.
    public override async Task<SignInResult> PasswordSignInAsync(string userName, string password, bool isPersistent, bool lockoutOnFailure)
    {
        ArgumentNullException.ThrowIfNull(userName);
        ArgumentNullException.ThrowIfNull(password);

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

    public override async Task<SignInResult> TwoFactorRecoveryCodeSignInAsync(string recoveryCode)
    {
        var user = await GetTwoFactorAuthenticationUserAsync();
        var result = await base.TwoFactorRecoveryCodeSignInAsync(recoveryCode);
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
