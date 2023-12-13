namespace FluentCMS.Entities;

public class ContentTypeField
{
    public string Name { get; set; } = default!;
    public string Description { get; set; } = default!;
    public string Label { get; set; } = default!;
    public string? Placeholder { get; set; }
    public string? Hint { get; set; }
    public string? DefaultValue { get; set; }
    public bool IsRequired { get; set; }
}
