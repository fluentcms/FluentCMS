namespace FluentCMS.Repositories.Caching;

public abstract class SiteAssociatedRepository<TEntity>(ISiteAssociatedRepository<TEntity> siteAssociatedRepository, ICacheProvider cacheProvider) : AuditableEntityRepository<TEntity>(siteAssociatedRepository, cacheProvider), ISiteAssociatedRepository<TEntity> where TEntity : ISiteAssociatedEntity
{
    public virtual async Task<IEnumerable<TEntity>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default)
    {
        var entities = await GetAll(cancellationToken);
        return entities.Where(e => e.SiteId == siteId);
    }
}
