namespace FluentCMS.Entities;

public class PluginDefinition : AuditableEntity
{
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public string Type { get; set; } = default!;
}
