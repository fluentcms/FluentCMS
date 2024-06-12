namespace FluentCMS.Repositories.LiteDb;

public abstract class SiteAssociatedRepository<TEntity> : AuditableEntityRepository<TEntity>, ISiteAssociatedRepository<TEntity>
    where TEntity : ISiteAssociatedEntity
{
    public SiteAssociatedRepository(ILiteDBContext liteDbContext, IAuthContext authContext) : base(liteDbContext, authContext)
    {
    }

    public async Task<IEnumerable<TEntity>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Collection.Query().Where(x => x.SiteId == siteId).ToListAsync();
    }

    public async Task<TEntity?> GetByIdForSite(Guid id, Guid siteId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Collection.Query().Where(x => x.Id == id && x.SiteId == siteId).SingleOrDefaultAsync();
    }
}
