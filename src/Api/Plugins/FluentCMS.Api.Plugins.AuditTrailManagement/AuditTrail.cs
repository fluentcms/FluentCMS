namespace FluentCMS.Api.Plugins.AuditTrailManagement;

public class AuditTrail : Entity
{
    public object Entity { get; set; } = default!;
    public string EventType { get; set; } = default!;
    public DateTime Timestamp { get; set; }
    public ISecurityContext? Context { get; set; }
}
