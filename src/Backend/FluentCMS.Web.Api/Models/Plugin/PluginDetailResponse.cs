namespace FluentCMS.Web.Api.Models;

public class PluginDetailResponse : BaseSiteAssociatedResponse
{
    public Guid DefinitionId { get; set; }
    public Guid PageId { get; set; }
    public int Order { get; set; } = 0;
    public int Cols { get; set; } = 0;
    public int ColsMd { get; set; } = 0;
    public int ColsLg { get; set; } = 0;
    public string Section { get; set; } = default!;
    public PluginDefinitionDetailResponse Definition { get; set; } = default!;
    public bool Locked { get; set; } = false;
}
