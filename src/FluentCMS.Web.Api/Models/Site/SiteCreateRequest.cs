namespace FluentCMS.Web.Api.Models;

public class SiteCreateRequest
{
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public List<string> Urls { get; set; } = [];
    public List<string> Languages { get; set; } = [];
    public string LayoutName { get; set; } = default!;
    public string DefaultPageTitle { get; set; } = default!;
}
