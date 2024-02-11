namespace FluentCMS.Web.Api.Models;

public class AppCreateRequest
{
    public string Slug { get; set; } = default!;

    [Required]
    public string Title { get; set; } = default!;
    public string? Description { get; set; }
}
