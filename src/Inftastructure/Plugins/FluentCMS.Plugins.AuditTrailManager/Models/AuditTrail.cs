namespace FluentCMS.Plugins.AuditTrailManager.Models;

public class AuditTrail : Entity
{
    public object Entity { get; set; } = default!;
    public string EventType { get; set; } = default!;
    public DateTime Timestamp { get; set; }
    public IApplicationExecutionContext Context { get; set; } = default!;
}
