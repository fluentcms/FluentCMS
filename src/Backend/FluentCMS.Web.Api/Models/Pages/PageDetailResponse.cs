namespace FluentCMS.Web.Api.Models;

public class PageDetailResponse : BaseSiteAssociatedResponse
{
    public Guid? ParentId { get; set; }
    public string Title { get; set; } = default!;
    public List<PageDetailResponse> Children { get; set; } = [];
    public int Order { get; set; }
    public string Path { get; set; } = default!;
    public string FullPath { get; set; } = default!;
    public Guid? LayoutId { get; set; } 
    public Guid? DetailLayoutId { get; set; } 
    public Guid? EditLayoutId { get; set; } 
    public bool Locked { get; set; } = false;
    public IEnumerable<Guid> ViewRoleIds { get; set; } = [];
    public IEnumerable<Guid> ContributorRoleIds { get; set; } = [];
    public IEnumerable<Guid> AdminRoleIds { get; set; } = [];
}
