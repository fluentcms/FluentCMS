namespace FluentCMS.Entities;

public class Content : AuditableEntity
{
    public Guid TypeId { get; set; }
    public Dictionary<string, object?> Value { get; set; } = [];
}
