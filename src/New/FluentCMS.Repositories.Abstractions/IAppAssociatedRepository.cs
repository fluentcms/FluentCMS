namespace FluentCMS.Repositories.Abstractions;

public interface IAppAssociatedRepository<TEntity> : IAuditableEntityRepository<TEntity> where TEntity : IAppAssociatedEntity
{
    Task<IEnumerable<TEntity>> GetAll(Guid appId, CancellationToken cancellationToken = default);
}
