using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Repositories;

public class AuditableEntityRepository<TEntity, TDataContext>(TDataContext dataContext) : EntityRepository<TEntity, TDataContext>(dataContext), IAuditableEntityRepository<TEntity>
    where TEntity : class, IAuditableEntity
    where TDataContext : IDataContext
{
}
