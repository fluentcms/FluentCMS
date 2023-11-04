namespace FluentCMS.Entities;

public class Site : AuditEntity
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required List<string> Urls { get; set; } = [];
    //public required Guid RoleId { get; set; } 
}
