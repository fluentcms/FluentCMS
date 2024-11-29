namespace FluentCMS.Entities;

public class PluginContent : SiteAssociatedEntity
{
    public Guid PluginId { get; set; }
    public string Type { get; set; } = default!;
    public Dictionary<string, object?> Data { get; set; } = [];
}

