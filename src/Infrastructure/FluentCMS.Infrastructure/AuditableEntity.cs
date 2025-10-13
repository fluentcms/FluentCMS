namespace FluentCMS.Infrastructure;

public interface IAuditableEntity : IEntity
{
    DateTime CreatedAt { get; set; }
    DateTime? UpdatedAt { get; set; }
    string? CreatedBy { get; set; }
    string? UpdatedBy { get; set; }
    int Version { get; set; }
}

public abstract class AuditableEntity : Entity, IAuditableEntity
{
    [ConcurrencyCheck]
    public virtual DateTime CreatedAt { get; set; }
    public virtual DateTime? UpdatedAt { get; set; }

    [ConcurrencyCheck]
    public virtual string? CreatedBy { get; set; }
    public virtual string? UpdatedBy { get; set; }

    [ConcurrencyCheck]
    public virtual int Version { get; set; }
}
