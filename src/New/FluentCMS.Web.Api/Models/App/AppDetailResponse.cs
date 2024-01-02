namespace FluentCMS.Web.Api.Models;

public class AppDetailResponse : BaseAppAssociatedResponse
{
    public string Slug { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string? Description { get; set; }
}
