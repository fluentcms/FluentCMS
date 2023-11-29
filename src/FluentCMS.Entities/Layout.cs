namespace FluentCMS.Entities;

public class Layout : AuditEntity, IAuthorizeEntity
{
    public Guid SiteId { get; set; }
    public string Name { get; set; } = default!;
    public string Content { get; set; } = default!;
    public string Head { get; set; } = default!;
}
