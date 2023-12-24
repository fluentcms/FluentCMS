using FluentCMS.Entities;

public class AppTemplate : AuditableEntity
{
    public string Name { get; set; } = default!;
    public string Description { get; set; } = default!;
    public List<ContentTypeTemplate> ContentTypes { get; set; } = [];
}
