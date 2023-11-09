using FluentCMS.Entities.Identity;
using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Services.Identity;

public class UserRoleService : IUserRoleService

{
    private readonly UserManager<User> _userManager;

    public UserRoleService(UserManager<User> userManager)
    {
        _userManager = userManager;
    }

    public virtual async Task<bool> AddRoles(Guid userId, IEnumerable<string> roleNames, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await _userManager.FindByIdAsync(userId.ToString());

        var idResult = await _userManager.AddToRolesAsync(user, roleNames);

        idResult.ThrowIfInvalid();

        return true;
    }

    public virtual async Task<IList<string>> GetRolesForUser(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await _userManager.FindByIdAsync(userId.ToString());

        return await _userManager.GetRolesAsync(user);
    }

    public virtual async Task<IList<User>> GetUsersInRole(string roleName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await _userManager.GetUsersInRoleAsync(roleName);
    }

    public virtual async Task<bool> RemoveRoles(Guid userId, IEnumerable<string> roleNames, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await _userManager.FindByIdAsync(userId.ToString());

        var idResult = await _userManager.RemoveFromRolesAsync(user, roleNames);

        idResult.ThrowIfInvalid();

        return true;
    }
}