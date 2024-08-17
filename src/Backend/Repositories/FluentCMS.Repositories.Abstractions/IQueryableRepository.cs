using System.Linq;
namespace FluentCMS.Repositories.Abstractions;

public interface IQueryableRepository<TEntity> where TEntity : IEntity
{
    IQueryable<TEntity> AsQueryable();
}
