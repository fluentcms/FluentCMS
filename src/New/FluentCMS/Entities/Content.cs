namespace FluentCMS.Entities;

public class Content : AppAssociatedEntity
{
    public Guid TypeId { get; set; }
    public Dictionary<string, object?> Value { get; set; } = [];
}
