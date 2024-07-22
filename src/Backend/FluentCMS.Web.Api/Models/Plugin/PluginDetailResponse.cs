namespace FluentCMS.Web.Api.Models;

public class PluginDetailResponse : BaseSiteAssociatedResponse
{
    public Guid DefinitionId { get; set; }
    public Guid PageId { get; set; }
    public int Order { get; set; } = 0;
    public PluginDefinitionDetailResponse Definition { get; set; } = default!;
    public bool Locked { get; set; } = false;
}
