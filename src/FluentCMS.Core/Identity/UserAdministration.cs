using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Identity;

internal sealed record UserRoleInfo(Guid Id, string Name, Guid? SiteId);

internal sealed record UserInfo(
    Guid Id,
    string UserName,
    string? Email,
    bool EmailConfirmed,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeactivatedAt,
    bool MustChangePassword,
    bool TwoFactorEnabled,
    DateTimeOffset? LockoutEnd,
    IReadOnlyList<UserRoleInfo> Roles);

// What an admin changes on a user; a null member leaves the value as it is.
internal sealed record UserChange
{
    public string? UserName { get; init; }

    public string? Email { get; init; }

    public bool? MustChangePassword { get; init; }
}

internal sealed record UserPage(IReadOnlyList<UserInfo> Items, long Total, int Page, int PageSize);

// Admin users for the Management API. A user belongs to no site, so everything here needs users.manage for the instance.
// Changing a user, deactivating or reactivating it or ending its sessions also needs every grant of the user's roles, so no
// one acts on a user who can do more; a role is given or taken only by one who holds all it grants.
internal sealed class UserAdministration(
    CmsUserManager users,
    SystemDbContext context,
    ManagementAuthority authority,
    RoleService roles,
    UserSessions sessions,
    IOperationContextAccessor accessor)
{
    public async Task<UserPage> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        await RequireAsync(cancellationToken);

        var query = users.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search) && users.NormalizeName(search.Trim()) is { } term)
        {
            query = query.Where(user => user.NormalizedUserName!.Contains(term) || user.NormalizedEmail!.Contains(term));
        }

        var total = await query.LongCountAsync(cancellationToken);
        var found = await query.OrderBy(user => user.CreatedAt).ThenBy(user => user.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        var held = await UserViews.RolesOfAsync(context, [.. found.Select(user => user.Id)], cancellationToken);

        return new UserPage([.. found.Select(user => UserViews.Info(user, held[user.Id]))], total, page, pageSize);
    }

    public async Task<UserInfo> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await RequireAsync(cancellationToken);
        return await InfoAsync(await FindAsync(id), cancellationToken);
    }

    // An address an admin sets counts as confirmed, as an invitation's does. A new name or address ends the user's sessions
    // with a new security stamp, as a new password does.
    public async Task<UserInfo> UpdateAsync(Guid id, UserChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        var user = await TargetAsync(id, cancellationToken);
        var (oldName, oldEmail) = (user.UserName, user.Email);
        if (change.UserName is { } userName)
        {
            user.UserName = userName.Trim();
        }

        if (change.Email is { } email)
        {
            user.Email = email.Trim();
        }

        if (change.MustChangePassword is { } mustChange)
        {
            user.MustChangePassword = mustChange;
        }

        UserViews.Check(await users.UpdateAsync(user));
        if (user.UserName != oldName || user.Email != oldEmail)
        {
            UserViews.Check(await users.UpdateSecurityStampAsync(user));
        }

        return await InfoAsync(user, cancellationToken);
    }

    // A user who deactivates itself would leave nobody to undo it.
    public async Task<UserInfo> DeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await TargetAsync(id, cancellationToken);
        if (accessor.Current?.Principal is { Kind: PrincipalKind.AdminUser } caller && caller.Id == id)
        {
            throw new ValidationException([new ValidationError("id", IdentityErrorCodes.InvalidUser, "A user cannot deactivate itself.")]);
        }

        UserViews.Check(await users.DeactivateAsync(user));
        return await InfoAsync(user, cancellationToken);
    }

    public async Task<UserInfo> ReactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await TargetAsync(id, cancellationToken);
        UserViews.Check(await users.ReactivateAsync(user));
        return await InfoAsync(user, cancellationToken);
    }

    public async Task<UserInfo> AddRoleAsync(Guid id, Guid roleId, CancellationToken cancellationToken)
    {
        await RequireAsync(cancellationToken);
        var user = await FindAsync(id);
        await roles.RequireAssignableAsync([roleId], cancellationToken);

        UserViews.Check(await users.AddToRoleAsync(user, roleId));
        return await InfoAsync(user, cancellationToken);
    }

    public async Task<UserInfo> RemoveRoleAsync(Guid id, Guid roleId, CancellationToken cancellationToken)
    {
        await RequireAsync(cancellationToken);
        var user = await FindAsync(id);
        await roles.RequireAssignableAsync([roleId], cancellationToken);

        UserViews.Check(await users.RemoveFromRoleAsync(user, roleId));
        return await InfoAsync(user, cancellationToken);
    }

    public async Task<IReadOnlyList<UserSession>> SessionsAsync(Guid id, CancellationToken cancellationToken)
    {
        await RequireAsync(cancellationToken);
        var user = await FindAsync(id);
        return await sessions.ListAsync(user.Id, cancellationToken);
    }

    public async Task RevokeSessionAsync(Guid id, Guid sessionId, CancellationToken cancellationToken)
    {
        var user = await TargetAsync(id, cancellationToken);
        if (!await sessions.RevokeAsync(user.Id, sessionId, cancellationToken))
        {
            throw UserViews.SessionNotFound(sessionId);
        }
    }

    private ValueTask RequireAsync(CancellationToken cancellationToken) =>
        authority.RequireAsync(CorePermissions.UsersManage, site: null, cancellationToken);

    private async Task<CmsUser> TargetAsync(Guid id, CancellationToken cancellationToken)
    {
        await RequireAsync(cancellationToken);
        var user = await FindAsync(id);
        await roles.RequireAssignableAsync(await users.GetRoleIdsAsync(user), cancellationToken);
        return user;
    }

    private async Task<CmsUser> FindAsync(Guid id) =>
        await users.FindByIdAsync(id.ToString("D"))
            ?? throw new ValidationException([new ValidationError("id", IdentityErrorCodes.UserNotFound, $"There is no user {id:D}.")]);

    private async Task<UserInfo> InfoAsync(CmsUser user, CancellationToken cancellationToken) =>
        UserViews.Info(user, (await UserViews.RolesOfAsync(context, [user.Id], cancellationToken))[user.Id]);
}

// The user as the Management API shows it, and Identity's refusals as problems with the codes of the registry.
internal static class UserViews
{
    public static async Task<ILookup<Guid, UserRoleInfo>> RolesOfAsync(SystemDbContext context, List<Guid> userIds, CancellationToken cancellationToken)
    {
        var rows = await context.Set<CmsUserRole>().AsNoTracking()
            .Where(membership => userIds.Contains(membership.UserId))
            .Join(context.Set<Role>(), membership => membership.RoleId, role => role.Id, (membership, role) => new { membership.UserId, role.Id, role.Name, role.SiteId })
            .ToListAsync(cancellationToken);

        return rows.OrderBy(row => row.SiteId is not null).ThenBy(row => row.Name, StringComparer.Ordinal)
            .ToLookup(row => row.UserId, row => new UserRoleInfo(row.Id, row.Name, row.SiteId));
    }

    public static UserInfo Info(CmsUser user, IEnumerable<UserRoleInfo> roles) =>
        new(user.Id, user.UserName ?? "", user.Email, user.EmailConfirmed, user.CreatedAt, user.DeactivatedAt, user.MustChangePassword, user.TwoFactorEnabled, user.LockoutEnd, [.. roles]);

    public static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new ValidationException([.. result.Errors.Select(error => new ValidationError(PathOf(error.Code), CodeOf(error.Code), error.Description))]);
        }
    }

    public static ValidationException SessionNotFound(Guid sessionId) =>
        new([new ValidationError("sessionId", IdentityErrorCodes.SessionNotFound, $"There is no live session {sessionId:D} of the user.")]);

    private static string CodeOf(string code) => code switch
    {
        nameof(IdentityErrorDescriber.DuplicateEmail) => IdentityErrorCodes.EmailTaken,
        nameof(IdentityErrorDescriber.InvalidEmail) => IdentityErrorCodes.InvalidEmail,
        nameof(IdentityErrorDescriber.UserAlreadyInRole) => IdentityErrorCodes.RoleAlreadyHeld,
        nameof(IdentityErrorDescriber.UserNotInRole) => IdentityErrorCodes.RoleNotHeld,
        _ when code.StartsWith("FCMS", StringComparison.Ordinal) => code,
        _ => IdentityErrorCodes.InvalidUser,
    };

    private static string PathOf(string code) => code switch
    {
        nameof(IdentityErrorDescriber.DuplicateUserName) or nameof(IdentityErrorDescriber.InvalidUserName) => "userName",
        nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.InvalidEmail) => "email",
        nameof(IdentityErrorDescriber.UserAlreadyInRole) or nameof(IdentityErrorDescriber.UserNotInRole) or IdentityErrorCodes.UnknownRole => "roleId",
        _ => "",
    };
}
