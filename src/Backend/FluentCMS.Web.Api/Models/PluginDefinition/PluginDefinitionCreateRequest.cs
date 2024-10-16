namespace FluentCMS.Web.Api.Models;

public class PluginDefinitionCreateRequest
{
    public string Name { get; set; } = default!;
    public string Category { get; set; } = default!;
    public string Assembly { get; set; } = default!;
    public string? Description { get; set; }
    public IEnumerable<PluginDefinitionType> Types { get; set; } = [];
    public bool Locked { get; set; } = false;
}
