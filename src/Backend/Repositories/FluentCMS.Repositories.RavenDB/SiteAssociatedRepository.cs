namespace FluentCMS.Repositories.RavenDB;

public abstract class SiteAssociatedRepository<TEntity>(IRavenDBContext RavenDbContext, IApiExecutionContext apiExecutionContext) : AuditableEntityRepository<TEntity>(RavenDbContext, apiExecutionContext), ISiteAssociatedRepository<TEntity> where TEntity : ISiteAssociatedEntity
{
    public override async Task<TEntity?> Update(TEntity entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            Guid id = entity.Id;
            var dbEntity = await session.Query<RavenEntity<TEntity>>().SingleOrDefaultAsync(x => x.Data.Id == id, cancellationToken);
            if (dbEntity == null)
            {
                SetAuditableFieldsForCreate(entity);

                dbEntity = new RavenEntity<TEntity>(entity);

                await session.StoreAsync(dbEntity, cancellationToken);
            }
            else
            {
                Guid siteId = dbEntity.Data.SiteId;

                entity.CopyProperties(dbEntity.Data);

                dbEntity.Data.SiteId = siteId;

                SetAuditableFieldsForUpdate(dbEntity.Data);
            }

            if (entity.SiteId != Guid.Empty)
                dbEntity.Data.SiteId = entity.SiteId;

            await session.SaveChangesAsync();

            return dbEntity.Data;
        }
    }

    public async Task<IEnumerable<TEntity>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            var qres = await session.Query<RavenEntity<TEntity>>()
                                    .Where(x => x.Data.SiteId == siteId)
                                    .Select(x => x.Data)
                                    .ToListAsync(cancellationToken);

            return qres.AsEnumerable();
        }
    }

    public async Task<SiteAssociatedEntity?> GetByIdForSite(Guid id, Guid siteId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            var entity = await session.Query<RavenEntity<SiteAssociatedEntity>>().SingleOrDefaultAsync(x => x.Data.Id == id && x.Data.SiteId == siteId, cancellationToken);

            return entity?.Data;
        }
    }
}
