using FluentCMS.Entities;

namespace FluentCMS.Repositories.Abstractions;

public interface IPermissionRepository
{
    Task<IEnumerable<Permission>> GetPermissions(Guid siteId, IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default);
}
