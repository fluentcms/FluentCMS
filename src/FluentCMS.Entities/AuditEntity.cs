namespace FluentCMS.Entities;

public interface IAuditEntity : IEntity
{
    string CreatedBy { get; set; } // UserName
    DateTime CreatedAt { get; set; }

    string LastUpdatedBy { get; set; } // UserName
    DateTime LastUpdatedAt { get; set; }
}

public class AuditEntity : IAuditEntity
{
    public Guid Id { get; set; } = default!;

    public string CreatedBy { get; set; } = string.Empty; // UserName
    public DateTime CreatedAt { get; set; }

    public string LastUpdatedBy { get; set; } = string.Empty; // UserName
    public DateTime LastUpdatedAt { get; set; }
}
