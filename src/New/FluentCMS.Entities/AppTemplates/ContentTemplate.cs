namespace FluentCMS.Entities;

public class ContentTemplate
{
    public string TypeSlug { get; set; } = default!;
    public Dictionary<string, object?> Value { get; set; } = [];
}
