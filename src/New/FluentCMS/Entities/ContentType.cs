namespace FluentCMS.Entities;

public class ContentType : AuditableEntity
{
    public string Title { get; set; } = default!;
    public string? Description { get; set; }
    public string Name { get; set; } = default!;
    public List<ContentTypeField> Fields { get; set; } = [];
}
