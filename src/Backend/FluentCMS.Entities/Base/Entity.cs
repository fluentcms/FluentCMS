namespace FluentCMS.Entities.Base;

public abstract class Entity : IEntity
{
    public Guid Id { get; set; }
}
