namespace FluentCMS.Entities;

public interface ICreationAuditableEntity : IEntity
{
    public string CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }
}

public class CreationAuditableEntity : ICreationAuditableEntity
{
    public Guid Id { get; set; }
    public string CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}
