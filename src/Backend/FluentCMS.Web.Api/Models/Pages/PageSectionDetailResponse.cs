namespace FluentCMS.Web.Api.Models;

public class PageSectionDetailResponse : BaseSiteAssociatedResponse
{
    public Guid? PageId { get; set; }
    public int Order { get; set; }
    public Dictionary<string, string> Styles { get; set; } = [];
    public List<PageColumnDetailResponse> Columns { get; set; } = [];
}
