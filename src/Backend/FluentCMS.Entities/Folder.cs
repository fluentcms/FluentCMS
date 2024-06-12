namespace FluentCMS.Entities;

public class Folder : AuditableEntity
{
    public string Name { get; set; } = default!;
    public Guid? ParentId { get; set; }
}
