namespace FluentCMS.Repositories.EFCore;

public interface ISiteAssociatedRepository<TEntity, TDBEntity> : ISiteAssociatedRepository<TEntity> where TEntity : ISiteAssociatedEntity where TDBEntity : ISiteAssociatedEntityModel
{
}

public abstract class SiteAssociatedRepository<TEntity, TDBEntity>(FluentCmsDbContext dbContext, IMapper mapper, IApiExecutionContext apiExecutionContext) : AuditableEntityRepository<TEntity, TDBEntity>(dbContext, mapper, apiExecutionContext), ISiteAssociatedRepository<TEntity, TDBEntity> where TEntity : SiteAssociatedEntity where TDBEntity : SiteAssociatedEntityModel
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
        return await DbContext.Set<TEntity>().Where(x => x.SiteId == siteId).ToListAsync(cancellationToken);
    }
}
