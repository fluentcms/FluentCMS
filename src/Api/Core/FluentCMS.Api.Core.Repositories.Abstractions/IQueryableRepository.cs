namespace FluentCMS.Api.Core.Repositories.Abstractions;

public interface IQueryableRepository<TEntity> where TEntity : IEntity
{
    IQueryable<TEntity> AsQueryable();
}
