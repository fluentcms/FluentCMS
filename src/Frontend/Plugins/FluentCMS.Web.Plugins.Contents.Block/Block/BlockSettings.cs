namespace FluentCMS.Web.Plugins.Contents.Block;

public class BlockSettings : IPluginSettings
{
    public Dictionary<string, string> Value { get; set; } = [];
}