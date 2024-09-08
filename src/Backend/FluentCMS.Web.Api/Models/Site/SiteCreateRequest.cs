namespace FluentCMS.Web.Api.Models;

public class SiteCreateRequest
{
    [Required]
    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    [Required]
    public string Template { get; set; } = default!;

    [Required]
    [DomainName]
    public string Url { get; set; } = default!;
}
