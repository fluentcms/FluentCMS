namespace FluentCMS.Entities;

public class ContentTypeField
{
    public string Slug { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string? Description { get; set; }
    public string Label { get; set; } = default!;
    public string? Placeholder { get; set; }
    public string? Hint { get; set; }
    public string? DefaultValue { get; set; }
    public bool IsRequired { get; set; }
    public bool IsPrivate { get; set; }
    public string FieldType { get; set; }
    public Dictionary<string, object> Metadata { get; set; }
}
