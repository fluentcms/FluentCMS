namespace FluentCMS.Services.Setup.Models;

internal class PluginDefinitionTemplate
{
    public string Name { get; set; } = default!;
    public string Category { get; set; } = default!;
    public string? Description { get; set; }
    public string Assembly { get; set; } = default!;
    public List<PluginDefinitionType> Types { get; set; } = [];
    public bool Locked { get; set; } = false;
}
