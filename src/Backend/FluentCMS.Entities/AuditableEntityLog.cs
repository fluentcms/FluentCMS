namespace FluentCMS.Entities;

public class AuditableEntityLog : Entity
{
    public string EntityType { get; set; } = default!;
    public Guid EntityId { get; set; }
    public string Action { get; set; } = default!;
    public DateTime TimeStamp { get; set; } = DateTime.UtcNow;
    public string Username { get; set; } = default!;
    public object? Entity { get; set; }
}
