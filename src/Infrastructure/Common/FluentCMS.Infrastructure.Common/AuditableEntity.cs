namespace FluentCMS.Infrastructure;

public interface IAuditableEntity : IEntity
{
    [ConcurrencyCheck]
    DateTime CreatedAt { get; set; }

    [ConcurrencyCheck]
    string? UpdatedBy { get; set; }

    DateTime? UpdatedAt { get; set; }

    string? CreatedBy { get; set; }

    [ConcurrencyCheck]
    int Version { get; set; }
}

public abstract class AuditableEntity : Entity, IAuditableEntity
{
    public virtual DateTime CreatedAt { get; set; }
    public virtual DateTime? UpdatedAt { get; set; }
    public virtual string? CreatedBy { get; set; }
    public virtual string? UpdatedBy { get; set; }
    public virtual int Version { get; set; }
}
