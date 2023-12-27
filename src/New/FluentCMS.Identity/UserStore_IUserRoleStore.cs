using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Identity;

public partial class UserStore : IUserRoleStore<User>
{
    public Task AddToRoleAsync(User user, string roleId, CancellationToken cancellationToken)
    {
        var id = Guid.Parse(roleId);

        // check if user is already in role
        if (user.RoleIds.Contains(id))
            user.RoleIds.Add(id);

        return Task.CompletedTask;
    }

    public Task RemoveFromRoleAsync(User user, string roleId, CancellationToken cancellationToken)
    {
        var id = Guid.Parse(roleId);

        user.RoleIds.Remove(id);

        return Task.CompletedTask;
    }

    public async Task<IList<User>> GetUsersInRoleAsync(string roleId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return (await repository.GetUsersInRole(roleId, cancellationToken)).ToList();
    }

    public Task<IList<string>> GetRolesAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var roleIds = user.RoleIds.Select(id => id.ToString()).ToList();

        return Task.FromResult((IList<string>)roleIds);
    }

    public Task<bool> IsInRoleAsync(User user, string roleId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var id = Guid.Parse(roleId);

        return Task.FromResult(user.RoleIds.Contains(id));
    }
}
