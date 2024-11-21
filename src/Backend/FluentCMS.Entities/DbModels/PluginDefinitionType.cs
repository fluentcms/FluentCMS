namespace FluentCMS.Repositories.EFCore.DbModels;

public class PluginDefinitionType : Entity
{
    public string Name { get; set; } = default!;
    public string Type { get; set; } = default!;
    public bool IsDefault { get; set; } = false;
    public Guid PluginDefinitionId { get; set; } // foreign key
    public PluginDefinition PluginDefinition { get; set; } = default!; // navigation property
}
