namespace FluentCMS.Repositories.EFCore.DbModels;

public class PluginDefinition : AuditableEntity
{
    public string Name { get; set; } = default!;
    public string Category { get; set; } = default!;
    public string Assembly { get; set; } = default!;
    public string? Icon { get; set; } = default!;
    public string? Description { get; set; }
    public ICollection<PluginDefinitionType> Types { get; set; } = [];
    public bool Locked { get; set; } = false;
}
