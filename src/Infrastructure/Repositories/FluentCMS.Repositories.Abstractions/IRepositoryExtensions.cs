using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Repositories;

public static class IRepositoryExtensions
{
    // Extension method for simplified retrieval by ID
    public static Task<TEntity> GetById<TEntity>(this IRepository<TEntity> repository, Guid id, CancellationToken cancellationToken = default)
        where TEntity : class, IEntity
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(repository);

        return repository.Query().Single(e => e.Id.Equals(id), cancellationToken);
    }
}
