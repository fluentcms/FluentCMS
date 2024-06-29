namespace FluentCMS.Web.Api.Models;

public class ContentTypeField
{
    public string Name { get; set; } = default!;
    public string Description { get; set; } = default!;
    public string Type { get; set; } = default!;
    public bool Required { get; set; }
    public bool Unique { get; set; }
    public string Label { get; set; } = default!;
    public Dictionary<string, object?>? Settings { get; set; } = default!;
}
