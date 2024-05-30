namespace FluentCMS.Entities;

public class ContentTypeField
{
    public string Slug { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string Type { get; set; } = default!;
    public string? Description { get; set; }
    public string? Placeholder { get; set; }
    public bool IsRequired { get; set; }
}
