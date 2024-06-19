namespace FluentCMS.Entities;

public interface IPluginContent : ISiteAssociatedEntity
{
    Guid PluginId { get; set; }
    string Type { get; set; }
    PluginContentValue Value { get; set; }
}

public class PluginContent : SiteAssociatedEntity, IPluginContent
{
    public Guid PluginId { get; set; }
    public string Type { get; set; } = default!;
    public PluginContentValue Value { get; set; } = [];
}

public class PluginContentValue : Dictionary<string, object>
{

}
