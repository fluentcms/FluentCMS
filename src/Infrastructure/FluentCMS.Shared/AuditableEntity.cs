namespace FluentCMS;

public abstract class AuditableEntity : Entity, IAuditableEntity
{
    [ConcurrencyCheck]
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    [ConcurrencyCheck]
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    [ConcurrencyCheck]
    public int Version { get; set; }
}