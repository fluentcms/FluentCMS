namespace FluentCMS.Web.Api.Models;

public class PageDetailResponse : BaseSiteAssociatedResponse
{
    public Guid? ParentId { get; set; }
    public Guid? LayoutId { get; set; } = default!;
    public string Title { get; set; } = default!;
    public List<PageDetailResponse> Children { get; set; } = [];
    public int Order { get; set; }
    public string Path { get; set; } = default!;
    public LayoutDetailResponse Layout { get; set; } = default!;
    public LayoutDetailResponse DetailLayout { get; set; } = default!;
    public LayoutDetailResponse EditLayout { get; set; } = default!;
    public bool Locked { get; set; } = false;
}
