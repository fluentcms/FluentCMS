using FluentCMS.Entities.Identity;

namespace FluentCMS.Services.Identity;

public interface IUserRoleService
{
    Task<bool> AddRoles(Guid userId, IEnumerable<string> roleNames, CancellationToken cancellationToken = default);
    Task<bool> RemoveRoles(Guid userId, IEnumerable<string> roleNames, CancellationToken cancellationToken = default);
    Task<IList<string>> GetRolesForUser(Guid userId, CancellationToken cancellationToken = default);
    Task<IList<User>> GetUsersInRole(string roleNames, CancellationToken cancellationToken = default);
}
