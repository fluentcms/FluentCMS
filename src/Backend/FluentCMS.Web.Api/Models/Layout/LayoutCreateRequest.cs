namespace FluentCMS.Web.Api.Models;

public class LayoutCreateRequest
{
    [Required]
    public Guid SiteId { get; set; } = Guid.Empty;

    [Required]
    public string Name { get; set; } = default!;
    public string Body { get; set; } = default!;
    public string Head { get; set; } = default!;
}
