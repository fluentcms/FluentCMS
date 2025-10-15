namespace FluentCMS.Api.Core.Models;

public class PluginDefinition : AuditableEntity
{
    public string Name { get; set; } = default!;
    public string Category { get; set; } = default!;
    public string Assembly { get; set; } = default!;
    public string? Icon { get; set; } = default!;
    public string? Description { get; set; }
}
