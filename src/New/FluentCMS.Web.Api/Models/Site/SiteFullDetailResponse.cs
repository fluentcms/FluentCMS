namespace FluentCMS.Web.Api.Models;

public class SiteFullDetailResponse : SiteDetailResponse
{
    public List<LayoutDetailResponse> Layouts { get; set; } = [];
}
