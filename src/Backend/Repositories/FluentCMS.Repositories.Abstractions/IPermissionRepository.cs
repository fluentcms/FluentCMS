namespace FluentCMS.Repositories.Abstractions;

public interface IPermissionRepository : ISiteAssociatedRepository<Permission>
{
    Task<IEnumerable<Permission>> Set(Guid siteId, Guid entityId, string entityTypeName, string action, IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default);
}
