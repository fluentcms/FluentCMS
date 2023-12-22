using FluentCMS.Entities;

namespace FluentCMS.Repositories;

public interface IAuditableEntityRepository<TEntity> : IEntityRepository<TEntity> where TEntity : IAuditableEntity
{
}
