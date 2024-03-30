namespace FluentCMS.Entities;

public interface IAuditableEntity : ICreationAuditableEntity
{
    string? ModifiedBy { get; set; }
    DateTime? ModifiedAt { get; set; }
}

public abstract class AuditableEntity : IAuditableEntity
{
    public Guid Id { get; set; }
    public string CreatedBy { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }
}
