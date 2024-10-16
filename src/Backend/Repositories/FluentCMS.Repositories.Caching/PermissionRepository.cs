namespace FluentCMS.Repositories.Caching;

public class PermissionRepository(IPermissionRepository repository, ICacheProvider cacheProvider) : SiteAssociatedRepository<Permission>(repository, cacheProvider), IPermissionRepository
{
    public async Task<IEnumerable<Permission>> Set(Guid siteId, Guid entityId, string entityTypeName, string action, IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default)
    {
        var results = await repository.Set(siteId, entityId, entityTypeName, action, roleIds, cancellationToken);
        InvalidateCache();
        return results;
    }
    public async Task<IEnumerable<Permission>> Get(Guid siteId, Guid entityId, string entityTypeName, string action, CancellationToken cancellationToken = default)
    {
        var results = await repository.Get(siteId, entityId, entityTypeName, action, cancellationToken);
        return results;
    }
}
