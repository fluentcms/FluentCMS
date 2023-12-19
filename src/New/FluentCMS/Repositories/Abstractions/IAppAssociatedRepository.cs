using FluentCMS.Entities;

namespace FluentCMS.Repositories;

public interface IAppAssociatedRepository<TEntity> : IAuditableEntityRepository<TEntity> where TEntity : IAppAssociatedEntity
{
    Task<IEnumerable<TEntity>> GetAll(Guid appId, CancellationToken cancellationToken = default);
}
