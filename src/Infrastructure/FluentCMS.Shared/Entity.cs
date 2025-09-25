namespace FluentCMS;

public interface IEntity
{
    Guid Id { get; set; }
}

public abstract class Entity : IEntity
{
    [Key]
    public Guid Id { get; set; }
}
