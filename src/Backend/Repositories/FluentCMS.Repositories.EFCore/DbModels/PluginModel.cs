namespace FluentCMS.Repositories.EFCore.DbModels;

[Table("Plugins")]
public class PluginModel : SiteAssociatedEntityModel
{
    public Guid DefinitionId { get; set; }
    public Guid PageId { get; set; }
    public int Order { get; set; } = 0;
    public int Cols { get; set; } = 12;
    public int ColsMd { get; set; } = 0;
    public int ColsLg { get; set; } = 0;
    public string Section { get; set; } = default!;
    public bool Locked { get; set; } = false;
}
