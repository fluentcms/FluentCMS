using FluentCMS.Entities;

namespace FluentCMS.Repositories;

public interface IPermissionRepository : ISiteAssociatedRepository<Permission>
{
    Task<IEnumerable<Permission>> GetPermissions(Guid siteId, IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default);
}
