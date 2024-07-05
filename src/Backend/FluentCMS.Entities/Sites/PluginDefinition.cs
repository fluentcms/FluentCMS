namespace FluentCMS.Entities;

public class PluginDefinition : AuditableEntity
{
    public string Name { get; set; } = default!;
    public string Assembly { get; set; } = default!;
    public string? Description { get; set; }
    public IEnumerable<PluginDefinitionType> Types { get; set; } = [];
    public bool Locked { get; set; } = false;
}
