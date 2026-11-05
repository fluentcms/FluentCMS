using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity.ApiKeys;
using FluentCMS.Core.Storage;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Identity;

// An admin user holds the roles cms_user_role gives it now, and none while it is deactivated; an API key holds the role
// cms_api_key binds it to now, and none once it is revoked or expired. A roles claim either carries, such as one kept on a
// schedule, is not read. Service accounts and members keep their claim until their stores arrive.
internal sealed class UserPrincipalRoles(CmsDbContext context, ApiKeyPrincipals keys) : IPrincipalRoles
{
    public async ValueTask<IReadOnlyCollection<Guid>> RolesOfAsync(Principal principal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);

        if (principal.Kind == PrincipalKind.ApiKey)
        {
            return await keys.ActiveAsync(principal.Id, cancellationToken) is { } key ? [key.RoleId] : [];
        }

        if (principal.Kind != PrincipalKind.AdminUser)
        {
            return ClaimPrincipalRoles.FromClaim(principal);
        }

        return await context.Set<CmsUserRole>().AsNoTracking()
            .Where(membership => membership.UserId == principal.Id
                && context.Set<CmsUser>().Any(user => user.Id == membership.UserId && user.DeactivatedAt == null))
            .Select(membership => membership.RoleId)
            .ToListAsync(cancellationToken);
    }
}
