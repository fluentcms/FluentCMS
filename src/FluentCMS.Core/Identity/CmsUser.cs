using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Core.Identity;

// A row of cms_user: an admin user. Its roles are rows of cms_user_role on the roles of cms_role.
public sealed class CmsUser : IdentityUser<Guid>
{
    public CmsUser()
    {
        Id = Guid.CreateVersion7();
    }

    public DateTimeOffset CreatedAt { get; set; }

    // Set by CmsUserManager.DeactivateAsync. A deactivated user cannot sign in and holds no roles.
    public DateTimeOffset? DeactivatedAt { get; set; }

    // The hosted pages make the user choose a new password before anything else; a password change clears it.
    public bool MustChangePassword { get; set; }
}
