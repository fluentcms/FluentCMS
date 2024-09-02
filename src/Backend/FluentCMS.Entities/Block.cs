namespace FluentCMS.Entities;

public class Block : AuditableEntity
{
    public string Name { get; set; } = default!;
    public string Category { get; set; } = default!;
    public string? Description { get; set; }
    public string Content { get; set; } = default!;
}
