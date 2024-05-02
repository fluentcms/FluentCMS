namespace FluentCMS.Web.Api.Models;

public class ContentTypeDetailResponse : BaseAuditableResponse
{
    public string Slug { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string? Description { get; set; }
    public List<ContentTypeField> Fields { get; set; } = [];
}
