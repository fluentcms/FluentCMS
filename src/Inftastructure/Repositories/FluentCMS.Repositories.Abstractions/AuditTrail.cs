namespace FluentCMS.Repositories.Abstractions;

public class AuditTrail
{
    [Key]
    public Guid Id { get; set; }
    public object Entity { get; set; } = default!;
    public string EventType { get; set; } = default!;
    public DateTime Timestamp { get; set; }
    public IApplicationExecutionContext? Context { get; set; }
}
