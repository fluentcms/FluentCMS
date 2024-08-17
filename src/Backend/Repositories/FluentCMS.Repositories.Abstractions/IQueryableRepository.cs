using System.Linq;
using FluentCMS.Entities.Base;

namespace FluentCMS.Repositories.Abstractions;

public interface IQueryableRepository<TEntity> where TEntity : IEntity
{
    IQueryable<TEntity> AsQueryable();
}
