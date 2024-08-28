namespace FluentCMS.Services.Setup.Models;

public class PluginDefinitionDetailModel : BaseAuditableModel
{
    public string Name { get; set; } = default!;
    public string Category { get; set; } = default!;
    public string Assembly { get; set; } = default!;
    public string? Description { get; set; }
    public List<PluginDefinitionType> Types { get; set; } = [];
    public bool Locked { get; set; } = false;
}
