namespace FluentCMS.Repositories.Abstractions;

public interface ISiteAssociatedRepository<TEntity> : IEntityRepository<TEntity> where TEntity : ISiteAssociatedEntity
{
    Task<IEnumerable<TEntity>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default);
    Task<TEntity?> GetByIdForSite(Guid id, Guid siteId, CancellationToken cancellationToken = default);
}
