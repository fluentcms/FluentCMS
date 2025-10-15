namespace FluentCMS.Api.Core.Repositories.Abstractions;

public interface ISiteAssociatedRepository<TEntity> : IRepository<TEntity>
    where TEntity : class, ISiteAssociatedEntity
{
    Task<IEnumerable<TEntity>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default);
}
