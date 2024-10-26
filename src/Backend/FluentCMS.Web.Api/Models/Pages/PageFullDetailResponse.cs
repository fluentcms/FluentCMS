namespace FluentCMS.Web.Api.Models;

public class PageFullDetailResponse : BaseSiteAssociatedResponse
{
    public Guid? ParentId { get; set; }
    public string Title { get; set; } = default!;
    public int Order { get; set; }
    public string Path { get; set; } = default!;
    public string FullPath { get; set; } = default!;
    public LayoutDetailResponse Layout { get; set; } = default!;
    public LayoutDetailResponse DetailLayout { get; set; } = default!;
    public SiteDetailResponse Site { get; set; } = default!;
    public Dictionary<string, List<PluginDetailResponse>> Sections { get; set; } = [];
    public bool Locked { get; set; } = false;
    public UserRoleDetailResponse User { get; set; } = default!;
    public List<Guid> ViewRoleIds { get; set; } = [];
    public List<Guid> AdminRoleIds { get; set; } = [];
}
