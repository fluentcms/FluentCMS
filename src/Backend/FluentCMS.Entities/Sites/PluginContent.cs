namespace FluentCMS.Entities;

public class PluginContent : SiteAssociatedEntity
{
    public Guid PluginId { get; set; }
    public string Type { get; set; } = default!;
    public PluginContentValue Value { get; set; } = [];
}

public class PluginContentValue : Dictionary<string, object?>
{

}
