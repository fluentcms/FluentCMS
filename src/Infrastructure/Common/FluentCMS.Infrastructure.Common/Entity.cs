namespace FluentCMS.Infrastructure;

public interface IEntity
{
    [Key]
    Guid Id { get; set; }
}

public class Entity : IEntity
{
    public virtual Guid Id { get; set; } = Guid.NewGuid();
}
