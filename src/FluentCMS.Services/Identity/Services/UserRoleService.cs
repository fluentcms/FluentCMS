using Microsoft.AspNetCore.Identity;

namespace uBeac.Identity;

public class UserRoleService<TUserKey, TUser> : IUserRoleService<TUserKey, TUser>
    where TUserKey : IEquatable<TUserKey>
    where TUser : User<TUserKey>
{
    private readonly UserManager<TUser> _userManager;

    public UserRoleService(UserManager<TUser> userManager)
    {
        _userManager = userManager;
    }

    public virtual async Task<bool> AddRoles(TUserKey userId, IEnumerable<string> roleNames, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await _userManager.FindByIdAsync(userId.ToString());

        var idResult = await _userManager.AddToRolesAsync(user, roleNames);

        idResult.ThrowIfInvalid();

        return true;
    }

    public virtual async Task<IList<string>> GetRolesForUser(TUserKey userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await _userManager.FindByIdAsync(userId.ToString());

        return await _userManager.GetRolesAsync(user);
    }

    public virtual async Task<IList<TUser>> GetUsersInRole(string roleName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await _userManager.GetUsersInRoleAsync(roleName);
    }

    public virtual async Task<bool> RemoveRoles(TUserKey userId, IEnumerable<string> roleNames, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await _userManager.FindByIdAsync(userId.ToString());

        var idResult = await _userManager.RemoveFromRolesAsync(user, roleNames);

        idResult.ThrowIfInvalid();

        return true;
    }
}

public class UserRoleService<TUser> : UserRoleService<Guid, TUser>, IUserRoleService<TUser>
    where TUser : User
{
    public UserRoleService(UserManager<TUser> userManager) : base(userManager)
    {
    }
}