namespace FluentCMS.Web.Api.Models;

public class PageRowDetailResponse : BaseSiteAssociatedResponse
{
    public Guid? SectionId { get; set; }
    public string Title { get; set; } = default!;
    public int Order { get; set; }
    public Dictionary<string, string> Styles { get; set; } = [];
    public List<PageColumnDetailResponse> Columns { get; set; } = [];
}
