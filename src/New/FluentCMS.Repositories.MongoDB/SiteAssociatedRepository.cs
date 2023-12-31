﻿namespace FluentCMS.Repositories.MongoDB;

public abstract class SiteAssociatedRepository<TEntity> : AuditableEntityRepository<TEntity>, ISiteAssociatedRepository<TEntity>
    where TEntity : ISiteAssociatedEntity
{
    public SiteAssociatedRepository(IMongoDBContext mongoDbContext, IAuthContext authContext) : base(mongoDbContext, authContext)
    {
    }

    public async Task<IEnumerable<TEntity>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var filter = Builders<TEntity>.Filter.Eq(x => x.SiteId, siteId);
        var findResult = await Collection.FindAsync(filter, null, cancellationToken);
        return await findResult.ToListAsync(cancellationToken);
    }

    public async Task<TEntity?> GetByIdForSite(Guid id, Guid siteId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var idFilter = Builders<TEntity>.Filter.Eq(x => x.Id, id);
        var siteIdFilter = Builders<TEntity>.Filter.Eq(x => x.SiteId, siteId);
        var combinedFilter = Builders<TEntity>.Filter.And(idFilter, siteIdFilter);
        var findResult = await Collection.FindAsync(combinedFilter, null, cancellationToken);
        return await findResult.SingleOrDefaultAsync(cancellationToken);
    }
}
