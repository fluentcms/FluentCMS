namespace FluentCMS.Entities;

public class Role : AuditEntity, IAuthorizeEntity
{
    public Guid SiteId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}
