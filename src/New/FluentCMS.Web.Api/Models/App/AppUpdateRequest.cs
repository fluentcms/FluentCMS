namespace FluentCMS.Web.Api.Models;

public class AppUpdateRequest
{
    public string Title { get; set; } = default!;
    public string? Description { get; set; }
    public string Slug { get; set; } = default!;
}
