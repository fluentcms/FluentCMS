namespace FluentCMS.Repositories.Postgres.Repositories;

public abstract class SiteAssociatedRepository<TEntity>(PostgresDbContext context) : AuditableEntityRepository<TEntity>(context), IService, ISiteAssociatedRepository<TEntity> where TEntity : SiteAssociatedEntity
{
    public override async Task<TEntity?> Update(TEntity entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var existing = await GetById(entity.Id, cancellationToken);
        if (existing is null)
            return default;

        entity.SiteId = existing.SiteId;

        return await base.Update(entity, cancellationToken);
    }

    public async Task<IEnumerable<TEntity>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Table.Where(x => x.SiteId == siteId).ToListAsync(cancellationToken);
    }
}
