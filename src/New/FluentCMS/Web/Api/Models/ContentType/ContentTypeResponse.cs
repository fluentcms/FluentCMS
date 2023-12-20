using FluentCMS.Entities;

namespace FluentCMS.Web.Api.Models;

public class ContentTypeResponse
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public string Slug { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string? Description { get; set; }
    public List<ContentTypeField> Fields { get; set; } = [];
}
