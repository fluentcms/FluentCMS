namespace FluentCMS.Repositories.EFCore.DbModels;

public class PluginContent : SiteAssociatedEntity
{
    public Guid PluginId { get; set; }
    public string Type { get; set; } = default!;
    public ICollection<PluginContentData> Data { get; set; } = [];
}
