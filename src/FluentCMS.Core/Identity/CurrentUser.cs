using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Core.Storage;

namespace FluentCMS.Core.Identity;

// The principal of the operation, as /me shows it. A stored user is shown with its roles; a key or a service account by
// its id, kind and name. Only a user has sessions, and it may end any of its own; only a user has two-factor sign-in and
// external logins.
internal sealed record CurrentPrincipal(Guid Id, PrincipalKind Kind, string Name, UserInfo? User);

internal sealed class CurrentUser(IOperationContextAccessor accessor, CmsUserManager users, SystemDbContext context, UserSessions sessions, TwoFactorService twoFactor, UserLogins logins)
{
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
        if (Caller() is not { Kind: PrincipalKind.AdminUser } principal || !await sessions.RevokeAsync(principal.Id, sessionId, cancellationToken))
        {
            throw UserViews.SessionNotFound(sessionId);
        }
    }

    public async Task<TwoFactorStatus> TwoFactorAsync(CancellationToken cancellationToken) =>
        await twoFactor.StatusAsync(await UserAsync(), cancellationToken);

    public async Task<AuthenticatorSetup> EnrolAuthenticatorAsync(string issuer) => await twoFactor.EnrolAsync(await UserAsync(), issuer);

    public async Task<IReadOnlyList<string>> VerifyAuthenticatorAsync(string? code) => await twoFactor.VerifyAsync(await UserAsync(), code);

    public async Task<IReadOnlyList<string>> RegenerateRecoveryCodesAsync(string? code) =>
        await twoFactor.RegenerateRecoveryCodesAsync(await UserAsync(), code);

    public async Task DisableTwoFactorAsync(string? code) => await twoFactor.DisableAsync(await UserAsync(), code);

    public async Task<IReadOnlyList<UserLogin>> LoginsAsync() => await logins.ListAsync(await UserAsync());

    public async Task RemoveLoginAsync(string provider, CancellationToken cancellationToken) =>
        await logins.RemoveAsync(await UserAsync(), provider, cancellationToken);

    private async Task<CmsUser> UserAsync() =>
        Caller() is { Kind: PrincipalKind.AdminUser } principal && await users.FindByIdAsync(principal.Id.ToString("D")) is { DeactivatedAt: null } user
            ? user
            : throw new ManagementAccessDeniedException("Two-factor sign-in and external logins belong to a user; the caller is not one.");

    private Principal Caller() =>
        accessor.Current?.Principal ?? throw new InvalidOperationException("The current user is that of an operation, and none is running.");
}
