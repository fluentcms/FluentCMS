namespace FluentCMS.Repositories.Abstractions;

public interface IDataContext
{
    // Persist all changes to the underlying store
    Task<int> SaveChanges(CancellationToken cancellationToken = default);

    // Get the entity set for a given entity type
    IEntitySet<TEntity> Set<TEntity>() where TEntity : class;
}

public interface IDataContext<TArea> : IDataContext
    where TArea : IDatabaseArea
{
}
