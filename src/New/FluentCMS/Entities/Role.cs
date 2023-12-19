namespace FluentCMS.Entities;

public class Role : AuditableEntity
{
    public required string Name { get; set; }
    public string? Description { get; set; }
}
