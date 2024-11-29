namespace FluentCMS.Repositories.MongoDB;

public abstract class SiteAssociatedRepository<TEntity>(IMongoDBContext mongoDbContext, IApiExecutionContext apiExecutionContext) : AuditableEntityRepository<TEntity>(mongoDbContext, apiExecutionContext), ISiteAssociatedRepository<TEntity> where TEntity : ISiteAssociatedEntity
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
        var filter = Builders<TEntity>.Filter.Eq(x => x.SiteId, siteId);
        var findResult = await Collection.FindAsync(filter, null, cancellationToken);
        return await findResult.ToListAsync(cancellationToken);
    }
}
