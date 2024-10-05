namespace FluentCMS.Web.Api.Models;

public class SiteCreateRequest
{
    [Required]
    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    [Required]
    public string Template { get; set; } = default!;

    [Required]
    public string Url { get; set; } = default!;
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public string? MetaKeywords { get; set; }
    public string? FaviconUrl { get; set; }
}
