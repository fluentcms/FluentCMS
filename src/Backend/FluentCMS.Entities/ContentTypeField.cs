namespace FluentCMS.Entities;

public class ContentTypeField
{
    public string Name { get; set; } = default!;
    public string Type { get; set; } = default!;
    public Dictionary<string, object?>? Settings { get; set; } = default!;
}
