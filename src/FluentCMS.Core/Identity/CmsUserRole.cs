namespace FluentCMS.Core.Identity;

// A row of cms_user_role: a user holds a role, instance-wide or of a site.
internal sealed class CmsUserRole
{
    public Guid UserId { get; set; }

    public Guid RoleId { get; set; }
}
