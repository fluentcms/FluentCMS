namespace FluentCMS.Repositories.Abstractions;

public interface IAppAssociatedRepository<TEntity> : IEntityRepository<TEntity> where TEntity : IAppAssociatedEntity
{
    Task<IEnumerable<TEntity>> GetAll(Guid appId, CancellationToken cancellationToken = default);
}
