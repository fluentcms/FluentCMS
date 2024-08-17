using FluentCMS.Entities.Base;

namespace FluentCMS.Entities;

public class Folder : AuditableEntity
{
    public string Name { get; set; } = default!;
    public Guid? FolderId { get; set; }
}
