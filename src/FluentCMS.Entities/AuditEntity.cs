namespace FluentCMS.Entities;

public interface IAuditEntity : IEntity
{
    // If system creates the entity, property should be null
    string? CreatedBy { get; set; } // UserName
    DateTime? CreatedAt { get; set; }

    // If system updates the entity, property should be null
    string? LastUpdatedBy { get; set; } // UserName
    DateTime? LastUpdatedAt { get; set; }
}

public class AuditEntity : Entity, IAuditEntity
{

    public string? CreatedBy { get; set; }
    public DateTime? CreatedAt { get; set; }

    public string? LastUpdatedBy { get; set; }
    public DateTime? LastUpdatedAt { get; set; }
}
