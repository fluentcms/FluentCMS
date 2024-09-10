namespace FluentCMS.Repositories.Abstractions;

public interface IPermissionRepository : ISiteAssociatedRepository<Permission>
{
    //Task<IEnumerable<Permission>> GetByEntityId(Guid entityId, CancellationToken cancellationToken);
    //Task<IEnumerable<Permission>> GetByActionAndEntityId(string action, Guid entityId, CancellationToken cancellationToken);
    //Task<IEnumerable<Permission>> Set(Guid siteId, string action, IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default);
    Task<IEnumerable<Permission>> Set(Guid siteId, Guid entityId, string entityTypeName, string action, IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default);
}
