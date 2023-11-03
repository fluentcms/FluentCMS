namespace FluentCMS.Entities;

public interface IEntity
{
    public Guid Id { get; set; }
}

public class Entity : IEntity
{
    public Guid Id { get; set; }
}

