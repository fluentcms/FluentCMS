namespace FluentCMS.Entities;

public class UserRole : AuditEntity
{
    public required Guid UserId { get; set; }
    public required Guid RoleId { get; set; }
    public required Guid SiteId { get; set; }
}
