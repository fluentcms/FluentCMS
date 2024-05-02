using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;
using LiteDB;

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
        var filter = Query.EQ(nameof(ISiteAssociatedEntity.SiteId), siteId);
        var findResult = await Collection.FindAsync(filter);
        return findResult.ToList();
    }

    public async Task<TEntity?> GetByIdForSite(Guid id, Guid siteId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var idFilter = Query.EQ(nameof(ISiteAssociatedEntity.Id), id);
        var siteIdFilter = Query.EQ(nameof(ISiteAssociatedEntity.SiteId), siteId);
        var combinedFilter = Query.And(idFilter, siteIdFilter);
        var findResult = await Collection.FindAsync(combinedFilter);
        return findResult.SingleOrDefault();
    }
}
