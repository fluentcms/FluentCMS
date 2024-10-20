namespace FluentCMS.Web.Api.Models;

public class PageParentDetailResponse : BaseSiteAssociatedResponse
{
    public PageFullDetailResponse? Parent { get; set; }
    public PageFullDetailResponse? Current { get; set; }
    public string Slug { get; set; } = string.Empty;
}
