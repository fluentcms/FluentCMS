namespace FluentCMS.Web.Api.Models;

public class LayoutCreateRequest
{
    [Required]
    public Guid SiteId { get; set; } = Guid.Empty;

    [Required]
    public string Name { get; set; } = default!;

    [Required]
    public string Body { get; set; } = default!;

    [Required]
    public string Head { get; set; } = default!;
}
