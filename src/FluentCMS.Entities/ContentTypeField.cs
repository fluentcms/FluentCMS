namespace FluentCMS.Entities;

public class ContentTypeField
{
    public string Name { get; set; } = default!;
    public string Type { get; set; } = default!;
    public string Label { get; set; } = default!;
    public string? Description { get; set; }
    public string? Placeholder { get; set; }
    public bool Required { get; set; }
}
