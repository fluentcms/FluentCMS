namespace FluentCMS.Repositories.Abstractions;

public interface ICachedRepository<TEntity> : IRepository<TEntity> where TEntity : class, IEntity
{
}