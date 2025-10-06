using FluentCMS.Providers.EventBus.Abstractions;

namespace FluentCMS.Repositories.EntityFramework;

/// <summary>
/// Event raised when an entity is created
/// </summary>
public class EntityCreatedEvent<TEntity>(TEntity entity) : EventBase<TEntity>(entity)
    where TEntity : class
{
}

/// <summary>
/// Event raised when an entity is updated
/// </summary>
public class EntityUpdatedEvent<TEntity>(TEntity entity) : EventBase<TEntity>(entity)
    where TEntity : class
{
}

/// <summary>
/// Event raised when an entity is deleted
/// </summary>
public class EntityDeletedEvent<TEntity>(TEntity entity) : EventBase<TEntity>(entity)
    where TEntity : class
{
}
