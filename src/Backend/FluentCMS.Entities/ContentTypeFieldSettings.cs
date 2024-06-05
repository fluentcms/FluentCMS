namespace FluentCMS.Entities;

public class ContentTypeFieldSettings
{
    public string Label { get; set; } = default!;
    public string? Description { get; set; }
    public bool Required { get; set; }
    public int Cols { get; set; }
    public int Order { get; set; }
    public Dictionary<string, object>? Meta { get; set; }
}
