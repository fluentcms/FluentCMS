namespace FluentCMS.Repositories.EFCore.DbModels;

[Table("PluginDefinitions")]
public class PluginDefinitionModel : AuditableEntityModel
{
    public string Name { get; set; } = default!;
    public string Category { get; set; } = default!;
    public string Assembly { get; set; } = default!;
    public string? Icon { get; set; } = default!;
    public string? Description { get; set; }
    public bool Locked { get; set; } = false;
    public ICollection<PluginDefinitionTypeModel> Types { get; set; } = []; // Navigation property
}
