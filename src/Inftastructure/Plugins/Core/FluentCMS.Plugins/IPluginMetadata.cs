namespace FluentCMS.Plugins;

public interface IPluginMetadata
{
    Assembly Assembly { get; set; }
    string FileName { get; set; }
    string Description { get; set; }
    string Name { get; set; }
    Type Type { get; set; }
    string Version { get; set; }
}
