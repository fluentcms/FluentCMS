using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Repositories.Abstractions;

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
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    [ConcurrencyCheck]
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    [ConcurrencyCheck]
    public int Version { get; set; }
}
