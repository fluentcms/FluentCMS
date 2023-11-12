namespace FluentCMS.Entities;

public class Site : AuditEntity
{
    public required string Name { get; set; } 
    public string? Description { get; set; } 
    public List<string> Urls { get; set; } = [];
    public List<Guid> AdminRoleIds { get; set; } = [];
}
