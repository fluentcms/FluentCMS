namespace FluentCMS.Entities;

public class Site : AuditEntity, ISecureEntity
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Urls { get; set; } = [];
    public List<Permission> Permissions { get; set; } = [];
}


