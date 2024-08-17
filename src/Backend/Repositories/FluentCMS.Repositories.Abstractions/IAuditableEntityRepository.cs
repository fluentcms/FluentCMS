using FluentCMS.Entities.Base;

namespace FluentCMS.Repositories.Abstractions;

public interface IAuditableEntityRepository<TEntity> : IEntityRepository<TEntity> where TEntity : IAuditableEntity
{
}
