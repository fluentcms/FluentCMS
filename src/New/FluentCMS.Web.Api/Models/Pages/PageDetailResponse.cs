namespace FluentCMS.Web.Api.Models;

public class PageDetailResponse : BaseSiteAssociatedResponse
{
    public Guid? ParentId { get; set; }
    public required string Title { get; set; }
    public List<PageDetailResponse> Children { get; set; } = [];
    public int Order { get; set; }
    public required string Path { get; set; }
}
