using FluentCMS.Entities.Identity;
using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Services.Identity.Stores;

public partial class UserStore : IUserRoleStore<User>
{
    public Task AddToRoleAsync(User user, string roleId, CancellationToken cancellationToken)
    {
        user.Roles.Add(roleId);
        return Task.CompletedTask;
    }

    public Task RemoveFromRoleAsync(User user, string roleId, CancellationToken cancellationToken)
    {
        user.Roles.Remove(roleId);
        return Task.CompletedTask;
    }

    public async Task<IList<User>> GetUsersInRoleAsync(string roleId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return (await _repository.GetUsersInRole(roleId, cancellationToken)).ToList();
    }

    public Task<IList<string>> GetRolesAsync(User user, CancellationToken cancellationToken)
    {
        return Task.FromResult((IList<string>)user.Roles);
    }

    public Task<bool> IsInRoleAsync(User user, string roleId, CancellationToken cancellationToken)
    {
        return Task.FromResult(user.Roles.Contains(roleId));
    }
}
