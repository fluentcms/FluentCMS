namespace FluentCMS.Web.Api.Models;

public class ContentTypeDetailResponse : BaseAppAssociatedResponse
{
    public string Slug { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string? Description { get; set; }
    public List<ContentTypeFieldResponse> Fields { get; set; } = [];
}
