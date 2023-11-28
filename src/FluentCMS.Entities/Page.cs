namespace FluentCMS.Entities;

public class Page : AuditEntity, IAuthorizeEntity
{
    public Guid SiteId { get; set; }
    public required string Title { get; set; }
    public Guid? ParentId { get; set; }
    public int Order { get; set; }
    public required string Path { get; set; }
    public Guid? LayoutId { get; set; } = null;
}
