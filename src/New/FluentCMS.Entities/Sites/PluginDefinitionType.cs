namespace FluentCMS.Entities;

public class PluginDefinitionType
{
    public string Name { get; set; } = default!;
    public string Type { get; set; } = default!;
    public bool IsDefault { get; set; } = false;
}
