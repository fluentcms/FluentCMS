namespace FluentCMS.Web.Api.Models;

public class ContentTypeUpdateRequest
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public string Title { get; set; } = default!;
    public string? Description { get; set; } = default!;
}
