using FluentCMS.Entities;

namespace FluentCMS.Repositories;

/// <summary>
/// Defines a repository for entities that include audit information.
/// </summary>
/// <typeparam name="TEntity">The type of entity this repository works with, constrained to IAuditEntity.</typeparam>
public interface IAuditEntityRepository<TEntity> : IEntityRepository<TEntity> where TEntity : IAuditEntity
{
    // Currently, this interface does not introduce additional methods.
    // It's defined to emphasize specialization for audit entities and can be extended in the future.
}
