using FluentCMS.EventBus.Abstractions;

namespace FluentCMS.Repositories.Abstractions;

public abstract class RepostoryEntityEvent(object entity) : EventBase
{
    public object Entity => entity;
    public abstract string EventType { get; }
}

/// <summary>
/// Event raised when an entity is created
/// </summary>
public class RepositoryEntityCreatedEvent(object entity) : RepostoryEntityEvent(entity)
{
    public override string EventType => "Create";
}

/// <summary>
/// Event raised when an entity is updated
/// </summary>
public class RepositoryEntityUpdatedEvent(object entity) : RepostoryEntityEvent(entity)
{
    public override string EventType => "Update";
}

/// <summary>
/// Event raised when an entity is deleted
/// </summary>
public class RepositoryEntityDeletedEvent(object entity) : RepostoryEntityEvent(entity)
{
    public override string EventType => "Delete";
}
