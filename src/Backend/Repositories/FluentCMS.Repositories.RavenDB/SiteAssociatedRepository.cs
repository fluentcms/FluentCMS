namespace FluentCMS.Repositories.RavenDB;

public abstract class SiteAssociatedRepository<TEntity>(IRavenDBContext RavenDbContext, IApiExecutionContext apiExecutionContext) : AuditableEntityRepository<TEntity>(RavenDbContext, apiExecutionContext), ISiteAssociatedRepository<TEntity> where TEntity : ISiteAssociatedEntity
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

        using (var session = Store.OpenAsyncSession())
        {
            var qres = await session.Query<TEntity>().Where(x => x.SiteId == siteId).ToListAsync(cancellationToken);

            return qres.AsEnumerable();
        }
    }

    public async Task<TEntity?> GetByIdForSite(Guid id, Guid siteId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            return await session.Query<TEntity>().SingleOrDefaultAsync(x => x.Id == id && x.SiteId == siteId, cancellationToken);
        }
    }
}
