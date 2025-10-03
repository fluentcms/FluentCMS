namespace FluentCMS.Repositories.Abstractions;

public interface IEntityRepository<TEntity> : IRepository<TEntity>
    where TEntity : class, IEntity
{
}
