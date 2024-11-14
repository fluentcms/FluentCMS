namespace FluentCMS.Repositories.EFCore;

public abstract class SiteAssociatedRepository<TEntity>(FluentCmsDbContext dbContext, IApiExecutionContext apiExecutionContext) : AuditableEntityRepository<TEntity>(dbContext, apiExecutionContext), ISiteAssociatedRepository<TEntity> where TEntity : SiteAssociatedEntity
{
    public override async Task<TEntity?> Update(TEntity entity, CancellationToken cancellationToken = default)
    {
        var existing = await GetById(entity.Id, cancellationToken);
        if (existing is null)
            return default;

        entity.SiteId = existing.SiteId;

        return await base.Update(entity, cancellationToken);
    }

    public virtual async Task<IEnumerable<TEntity>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default)
    {
        return await dbContext.Set<TEntity>().Where(x => x.SiteId == siteId).ToListAsync(cancellationToken);
    }
}
