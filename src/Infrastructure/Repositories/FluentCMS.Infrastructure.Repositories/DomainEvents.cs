namespace FluentCMS.Infrastructure.Repositories;

public abstract class RepositoryEntityEvent(object entity) : EventBase
{
    public object Entity => entity;
    public abstract string EventType { get; }
}

/// <summary>
/// Event raised when an entity is created
/// </summary>
public class RepositoryEntityCreatedEvent(object entity) : RepositoryEntityEvent(entity)
{
    public override string EventType => "Create";
}

/// <summary>
/// Event raised when an entity is updated
/// </summary>
public class RepositoryEntityUpdatedEvent(object entity) : RepositoryEntityEvent(entity)
{
    public override string EventType => "Update";
}

/// <summary>
/// Event raised when an entity is deleted
/// </summary>
public class RepositoryEntityDeletedEvent(object entity) : RepositoryEntityEvent(entity)
{
    public override string EventType => "Delete";
}
