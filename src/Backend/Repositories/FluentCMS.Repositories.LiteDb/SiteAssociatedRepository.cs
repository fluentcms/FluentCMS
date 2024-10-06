namespace FluentCMS.Repositories.LiteDb;

public abstract class SiteAssociatedRepository<TEntity>(ILiteDBContext liteDbContext, IApiExecutionContext apiExecutionContext) : AuditableEntityRepository<TEntity>(liteDbContext, apiExecutionContext), ISiteAssociatedRepository<TEntity> where TEntity : ISiteAssociatedEntity
{
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
}
