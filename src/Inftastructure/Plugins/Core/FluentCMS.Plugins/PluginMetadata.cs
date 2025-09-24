namespace FluentCMS.Plugins;

public class PluginMetadata : IPluginMetadata
{
    public string Name { get; set; } = default!;
    public string Version { get; set; } = default!;
    public string Description { get; set; } = default!;
    public Assembly Assembly { get; set; } = default!;
    public Type Type { get; set; } = default!;
    public string FileName { get; set; } = default!;
}