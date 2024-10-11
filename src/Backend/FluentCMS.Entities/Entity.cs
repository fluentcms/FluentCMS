namespace FluentCMS.Entities;

public interface IEntity
{
    string RavenId { get; set; }

    Guid Id { get; set; }
}

public abstract class Entity : IEntity
{
    public string RavenId { get; set; }
    public Guid Id { get; set; }
}
