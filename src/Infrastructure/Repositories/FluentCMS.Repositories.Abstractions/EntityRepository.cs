using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Repositories;

public class EntityRepository<TEntity, TDataContext>(TDataContext dataContext) : Repository<TEntity, TDataContext>(dataContext), IEntityRepository<TEntity>
    where TEntity : class, IEntity
    where TDataContext : IDataContext
{
}
