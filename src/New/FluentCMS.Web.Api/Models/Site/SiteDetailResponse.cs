namespace FluentCMS.Web.Api.Models;

public class SiteDetailResponse : BaseSiteAssociatedResponse
{
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public List<string> Urls { get; set; } = [];
}
