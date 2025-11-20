namespace FluentCMS.Api.Core.Repositories.EntityFramework;

// I moved site associated repository here, because it should be only available for CMS.
public abstract class SiteAssociatedRepository<TEntity>(CmsCoreDbContext dbContext) : RepositoryBase<TEntity>(dbContext), ISiteAssociatedRepository<TEntity>
    where TEntity : class, ISiteAssociatedEntity
{
    public virtual async Task<IEnumerable<TEntity>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Query().Where(x => x.SiteId == siteId).ToList(cancellationToken);
    }
}

public abstract class SiteAssociatedRepository<TEntity, TDataContext>(TDataContext dbContext) : Repository<TEntity, TDataContext>(dbContext), ISiteAssociatedRepository<TEntity>
    where TDataContext : DbContext
    where TEntity : class, ISiteAssociatedEntity
{
    public virtual async Task<IEnumerable<TEntity>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Query().Where(x => x.SiteId == siteId).ToList(cancellationToken);
    }
}
