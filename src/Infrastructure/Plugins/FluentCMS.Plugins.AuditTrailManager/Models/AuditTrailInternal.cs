namespace FluentCMS.Plugins.AuditTrailManager.Models;

public class AuditTrailInternal : Entity
{
    public Guid EntityId { get; set; }
    public string Entity { get; set; } = default!; // Json string representation of the entity
    public string EntityType { get; set; } = default!; // Type of the entity
    public string EventType { get; set; } = default!;
    public int EntityVersion { get; set; }
    public DateTime Timestamp { get; set; }

    // IApplicationExecutionContext fields
    public bool IsAuthenticated { get; set; }
    public string Language { get; set; } = default!;
    public string SessionId { get; set; } = default!;
    public DateTime StartDate { get; set; }
    public string TraceId { get; set; } = default!;
    public string UniqueId { get; set; } = default!;
    public Guid? UserId { get; set; }
    public string UserIp { get; set; } = default!;
    public string Username { get; set; } = default!;
}