namespace FluentCMS.Repositories.EFCore.DbModels;

[Table("PluginContents")]
public class PluginContentModel : SiteAssociatedEntityModel
{
    public Guid PluginId { get; set; }
    public string Type { get; set; } = default!;
    public string Data { get; set; } = default!;
}
