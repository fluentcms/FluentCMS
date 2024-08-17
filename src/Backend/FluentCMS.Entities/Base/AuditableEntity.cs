namespace FluentCMS.Entities.Base;

public abstract class AuditableEntity : Entity, IAuditableEntity
{
    public string CreatedBy { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }
}
