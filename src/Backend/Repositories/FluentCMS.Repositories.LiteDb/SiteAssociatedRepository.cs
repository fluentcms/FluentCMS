namespace FluentCMS.Repositories.LiteDb;

public abstract class SiteAssociatedRepository<TEntity> : AuditableEntityRepository<TEntity>, ISiteAssociatedRepository<TEntity>
    where TEntity : ISiteAssociatedEntity
{
    public SiteAssociatedRepository(ILiteDBContext liteDbContext, IApiExecutionContext apiExecutionContext) : base(liteDbContext, apiExecutionContext)
    {
    }

    public override async Task<TEntity?> Update(TEntity entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var existing = await GetById(entity.Id, cancellationToken);
        if (existing is null)
            return default;

        SetAuditableFieldsForUpdate(entity, existing);

        entity.SiteId = existing.SiteId;

        return await base.Update(entity, cancellationToken);
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
