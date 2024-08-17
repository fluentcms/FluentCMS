using FluentCMS.Entities.Base;

namespace FluentCMS.Entities;

public class ContentType : AuditableEntity
{
    public string Slug { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string? Description { get; set; }
    public List<ContentTypeField> Fields { get; set; } = [];
}
