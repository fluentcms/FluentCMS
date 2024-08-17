using FluentCMS.Entities.Base;

namespace FluentCMS.Entities;

public class File : AuditableEntity
{
    public string Name { get; set; } = default!;
    public Guid? FolderId { get; set; }
    public string Extension { get; set; } = default!;
    public string ContentType { get; set; } = default!;
    public long Size { get; set; }
}
