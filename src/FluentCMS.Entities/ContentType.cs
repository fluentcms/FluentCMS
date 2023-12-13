namespace FluentCMS.Entities;

public class ContentType : AuditEntity
{
    public string Title { get; set; } = default!;
    public string Description { get; set; } = default!;
    public string Name { get; set; } = default!; // the unique name of the type, this field won't be updated in future
    public List<ContentTypeField> Fields { get; set; } = [];
}
