namespace FluentCMS.Repositories.Caching;

public abstract class SiteAssociatedRepository<TEntity>(ISiteAssociatedRepository<TEntity> siteAssociatedRepository, ICacheProvider cacheProvider) : AuditableEntityRepository<TEntity>(siteAssociatedRepository, cacheProvider), ISiteAssociatedRepository<TEntity> where TEntity : ISiteAssociatedEntity
{
    public async Task<IEnumerable<TEntity>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default)
    {
        var entities = await siteAssociatedRepository.GetAll(cancellationToken);
        return entities.Where(e => e.SiteId == siteId);
    }

    public async Task<TEntity?> GetByIdForSite(Guid id, Guid siteId, CancellationToken cancellationToken = default)
    {
        var entity = await siteAssociatedRepository.GetById(id, cancellationToken);
        return entity?.SiteId == siteId ? entity : default;
    }
}
