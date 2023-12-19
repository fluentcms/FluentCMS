namespace FluentCMS.Entities;

public class Content : AppAssociatedEntity
{
    public string Type { get; set; } = default!;
    public Dictionary<string, object?> Value { get; set; } = [];
}
