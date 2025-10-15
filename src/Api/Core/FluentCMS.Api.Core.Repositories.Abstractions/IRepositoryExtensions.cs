namespace FluentCMS.Api.Core.Repositories.Abstractions;

public static class IRepositoryExtensions
{
    // Extension method for simplified retrieval by ID
    public static async Task<TEntity> GetById<TEntity>(this IRepository<TEntity> repository, Guid id, CancellationToken cancellationToken = default)
        where TEntity : class, IEntity
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(repository);

        return await repository.Query().SingleOrDefault(e => e.Id.Equals(id), cancellationToken) ??
            throw new EntityNotFoundException<TEntity>(id);
    }

    // Replaces the async TryGet method to avoid 'out' parameter (CS1988)
    public static async Task<(bool Success, TEntity? Entity)> TryGet<TEntity>(this IRepository<TEntity> repository, Guid id, CancellationToken cancellationToken = default)
        where TEntity : class, IEntity
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(repository);

        var entity = await repository.Query().SingleOrDefault(e => e.Id.Equals(id), cancellationToken);
        return (entity != null, entity);
    }

    public static async Task<bool> Exists<TEntity>(this IRepository<TEntity> repository, Guid id, CancellationToken cancellationToken = default)
        where TEntity : class, IEntity
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(repository);

        return await repository.Query().Any(e => e.Id.Equals(id), cancellationToken);
    }

    public static async Task<List<TEntity>> GetAll<TEntity>(this IRepository<TEntity> repository, CancellationToken cancellationToken = default)
       where TEntity : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(repository);

        return await repository.Query().ToList(cancellationToken);
    }

    public static async Task<TEntity> Remove<TEntity>(this IRepository<TEntity> repository, Guid id, CancellationToken cancellationToken = default)
        where TEntity : class, IEntity
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(repository);

        var existing = await repository.GetById(id, cancellationToken);

        return existing == null ?
            throw new EntityNotFoundException<TEntity>(id) :
            await repository.Remove(existing, cancellationToken);
    }
}
