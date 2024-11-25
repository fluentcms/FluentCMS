namespace FluentCMS.Repositories.EFCore.DbModels;

[Table("PluginDefinitionTypes")]
public class PluginDefinitionTypeModel : EntityModel
{
    public string Name { get; set; } = default!;
    public string Type { get; set; } = default!;
    public bool IsDefault { get; set; } = false;
    public Guid PluginDefinitionId { get; set; } // foreign key
    public PluginDefinitionModel PluginDefinition { get; set; } = default!; // navigation property
}
