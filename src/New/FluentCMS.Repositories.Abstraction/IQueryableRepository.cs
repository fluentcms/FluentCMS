using FluentCMS.Entities;

namespace FluentCMS.Repositories;

public interface IQueryableRepository<TEntity> where TEntity : IEntity
{
    IQueryable<TEntity> AsQueryable();
}
