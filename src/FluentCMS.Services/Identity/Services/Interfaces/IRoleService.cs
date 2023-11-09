using FluentCMS.Entities.Identity;

namespace FluentCMS.Services.Identity;

public interface IRoleService
{
    Task Create(Role role, CancellationToken cancellationToken = default);
    Task<bool> Delete(Guid id, CancellationToken cancellationToken = default);
    Task<bool> Update(Role role, CancellationToken cancellationToken = default);
    Task<bool> Exists(string roleName, CancellationToken cancellationToken = default);
    Task<IEnumerable<Role>> GetAll(CancellationToken cancellationToken = default);
    Task<Role> GetById(Guid id, CancellationToken cancellationToken = default);
}
