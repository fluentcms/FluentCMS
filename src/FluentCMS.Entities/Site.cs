namespace FluentCMS.Entities;

public class Site : AuditEntity, IAuthorizeEntity
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public List<string> Urls { get; set; } = [];
    public Guid SiteId { get; set; }
}
