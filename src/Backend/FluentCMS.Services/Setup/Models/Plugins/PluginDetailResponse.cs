namespace FluentCMS.Services.Setup.Models;

public class PluginDetailModel : BaseSiteAssociatedModel
{
    public Guid DefinitionId { get; set; }
    public Guid PageId { get; set; }
    public int Order { get; set; } = 0;
    public int Cols { get; set; } = 0;
    public int ColsMd { get; set; } = 0;
    public int ColsLg { get; set; } = 0;
    public string Section { get; set; } = default!;
    public PluginDefinitionDetailModel Definition { get; set; } = default!;
    public bool Locked { get; set; } = false;
    public Dictionary<string, string> Settings { get; set; } = [];
}
