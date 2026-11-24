using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Core.Storage;
using FluentCMS.Events;

namespace FluentCMS.Core.Identity;

// The principal of the operation, as /me shows it. A stored user is shown with its roles; a key or a service account by
// its id, kind and name. Only a user has sessions, and it may end any of its own; only a user has two-factor sign-in,
// external logins and preferences.
internal sealed record CurrentPrincipal(Guid Id, PrincipalKind Kind, string Name, UserInfo? User);

internal sealed class CurrentUser(IOperationContextAccessor accessor, CmsUserManager users, SystemDbContext context, UserSessions sessions, SessionEvents sessionEvents, TwoFactorService twoFactor, UserLogins logins, UserPreferences preferences, TimeProvider time)
{
    // How long after the sign-in on the hosted pages a token may still enrol an authenticator and turn two-factor on.
    internal static readonly TimeSpan RecentSignIn = TimeSpan.FromMinutes(10);

    public async Task<CurrentPrincipal> GetAsync(CancellationToken cancellationToken)
    {
        var principal = Caller();
        if (principal.Kind != PrincipalKind.AdminUser || await users.FindByIdAsync(principal.Id.ToString("D")) is not { } user)
        {
            return new CurrentPrincipal(principal.Id, principal.Kind, principal.Name, null);
        }

        var roles = await UserViews.RolesOfAsync(context, [user.Id], cancellationToken);
        return new CurrentPrincipal(user.Id, principal.Kind, principal.Name, UserViews.Info(user, user.DeactivatedAt is null ? roles[user.Id] : []));
    }

    public async Task<IReadOnlyList<UserSession>> SessionsAsync(CancellationToken cancellationToken) =>
        Caller() is { Kind: PrincipalKind.AdminUser } principal ? await sessions.ListAsync(principal.Id, cancellationToken) : [];

    public async Task RevokeSessionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        if (Caller() is not { Kind: PrincipalKind.AdminUser } principal
            || !await sessionEvents.EndAsync(
                () => sessions.RevokeAsync(principal.Id, sessionId, cancellationToken),
                new UserSessionsEndedEvent(principal.Id, principal.Name, SessionEndReasons.Revoked, sessionId),
                cancellationToken))
        {
            throw UserViews.SessionNotFound(sessionId);
        }
    }

    public async Task<TwoFactorStatus> TwoFactorAsync(CancellationToken cancellationToken) =>
        await twoFactor.StatusAsync(await UserAsync(), cancellationToken);

    public async Task<AuthenticatorSetup> EnrolAuthenticatorAsync(string issuer) => await twoFactor.EnrolAsync(await RecentlySignedInAsync(), issuer);

    public async Task<IReadOnlyList<string>> VerifyAuthenticatorAsync(string? code) => await twoFactor.VerifyAsync(await RecentlySignedInAsync(), code);

    public async Task<IReadOnlyList<string>> RegenerateRecoveryCodesAsync(string? code) =>
        await twoFactor.RegenerateRecoveryCodesAsync(await UserAsync(), code);

    public async Task DisableTwoFactorAsync(string? code) => await twoFactor.DisableAsync(await UserAsync(), code);

    public async Task<IReadOnlyList<UserLogin>> LoginsAsync() => await logins.ListAsync(await UserAsync());

    public async Task RemoveLoginAsync(string provider, CancellationToken cancellationToken) =>
        await logins.RemoveAsync(await UserAsync(), provider, cancellationToken);

    public async Task<UserPreferencesInfo> PreferencesAsync(CancellationToken cancellationToken) =>
        await preferences.GetAsync((await UserAsync()).Id, cancellationToken);

    public async Task<UserPreferencesInfo> SetPreferencesAsync(UserPreferencesChange change, string? expectedTag, CancellationToken cancellationToken) =>
        await preferences.SetAsync((await UserAsync()).Id, change, expectedTag, cancellationToken);

    // Turning two-factor on with an authenticator of one's own keeps the owner out, who has no code of it, so a token alone
    // is not enough: the user signed in on the hosted pages a short while ago, which a token that leaked later did not, and
    // refreshing a token does not make its sign-in newer. Turning it off or replacing the codes takes a code instead.
    private async Task<CmsUser> RecentlySignedInAsync()
    {
        var user = await UserAsync();
        if (TokenClaims.SignedInAt(Caller()) is not { } signedInAt || time.GetUtcNow() - signedInAt > RecentSignIn)
        {
            throw new ValidationException([new ValidationError("", IdentityErrorCodes.RecentSignInRequired, $"Setting up two-factor sign-in needs a sign-in of the last {RecentSignIn.TotalMinutes:0} minutes; sign in again, then set it up.")]);
        }

        return user;
    }

    private async Task<CmsUser> UserAsync() =>
        Caller() is { Kind: PrincipalKind.AdminUser } principal && await users.FindByIdAsync(principal.Id.ToString("D")) is { DeactivatedAt: null } user
            ? user
            : throw new ManagementAccessDeniedException("Two-factor sign-in, external logins and preferences belong to a user; the caller is not one.");

    private Principal Caller() =>
        accessor.Current?.Principal ?? throw new InvalidOperationException("The current user is that of an operation, and none is running.");
}
