using FluentCMS.Infrastructure.Exceptions;

namespace FluentCMS.Infrastructure.Repositories.Abstractions;

public static class IRepositoryExtensions
{
    // Extension method for simplified retrieval by ID
    public static async Task<TEntity> GetById<TEntity>(this IRepository<TEntity> repository, Guid id, CancellationToken cancellationToken = default)
        where TEntity : class, IEntity
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(repository);

        return await repository.Query().SingleOrDefault(e => e.Id.Equals(id), cancellationToken) ??
            throw new EntityNotFoundException();
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
        if (existing == null)
            throw new EntityNotFoundException();

        return await repository.Remove(existing, cancellationToken);
    }
}

public class ExceptionCodes
{
    public const string EntityNotFound = "entity_not_found";
}

public class EntityNotFoundException : EnhancedException
{
    public EntityNotFoundException() : base(ExceptionCodes.EntityNotFound)
    {
    }
}
