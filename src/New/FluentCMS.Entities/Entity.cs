namespace FluentCMS.Entities;

public interface IEntity
{
    Guid Id { get; set; }
}

public abstract class Entity : IEntity
{
    public Guid Id { get; set; }
}
