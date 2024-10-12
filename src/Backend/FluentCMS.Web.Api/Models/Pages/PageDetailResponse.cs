namespace FluentCMS.Web.Api.Models;

public class PageDetailResponse : BaseSiteAssociatedResponse
{
    public Guid? ParentId { get; set; }
    public string Title { get; set; } = default!;
    public List<PageDetailResponse> Children { get; set; } = [];
    public int Order { get; set; }
    public string Path { get; set; } = default!;
    public string FullPath { get; set; } = default!;
    public LayoutDetailResponse Layout { get; set; } = default!;
    public LayoutDetailResponse DetailLayout { get; set; } = default!;
    public LayoutDetailResponse EditLayout { get; set; } = default!;
    public Guid LayoutId { get; set; } = default!;
    public Guid DetailLayoutId { get; set; } = default!;
    public Guid EditLayoutId { get; set; } = default!;
    public bool Locked { get; set; } = false;
    public List<Guid> ViewRoleIds { get; set; } = [];
    public List<Guid> ContributorRoleIds { get; set; } = [];
    public List<Guid> AdminRoleIds { get; set; } = [];
}
