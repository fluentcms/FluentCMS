namespace FluentCMS.Repositories.EFCore.DbModels;

[Table("PluginContents")]
public class PluginContentModel : SiteAssociatedEntityModel
{
    public Guid PluginId { get; set; }
    public string Type { get; set; } = default!;
    public ICollection<PluginContentDataModel> Data { get; set; } = [];
    public PluginModel Plugin { get; set; } = default!; // Navigation property
}
