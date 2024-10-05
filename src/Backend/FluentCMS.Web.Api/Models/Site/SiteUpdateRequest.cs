namespace FluentCMS.Web.Api.Models;

public class SiteUpdateRequest
{
    [Required]
    public Guid Id { get; set; }

    [Required]
    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    [Required]
    public List<string> Urls { get; set; } = [];

    [Required]
    public Guid LayoutId { get; set; } = default!;

    [Required]
    public Guid DetailLayoutId { get; set; } = default!;

    [Required]
    public Guid EditLayoutId { get; set; } = default!;

    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public string? MetaKeywords { get; set; }
    public string? FaviconUrl { get; set; }
}
