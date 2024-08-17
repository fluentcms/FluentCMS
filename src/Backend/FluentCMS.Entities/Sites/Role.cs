using FluentCMS.Entities.Enums;

namespace FluentCMS.Entities.Sites;

public class Role : SiteAssociatedEntity
{
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public RoleTypes Type { get; set; }
}
