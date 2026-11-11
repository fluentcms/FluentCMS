using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity.ApiKeys;
using FluentCMS.Core.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Identity;

// An admin user holds the roles cms_user_role gives it now, and none while it is deactivated; an API key holds the role
// cms_api_key binds it to now, and none once it is revoked or expired. A roles claim either carries, such as one kept on a
// schedule, is not read. A service account keeps the claim of its client. A member's roles are those of its groups, which
// the evaluator reads itself, so a roles claim on a member gives nothing here. The key a request presented was read when
// it was authenticated, so that row is used until the request ends; a job reads the key again.
internal sealed class UserPrincipalRoles(CmsDbContext context, ApiKeyPrincipals keys, IHttpContextAccessor http, TimeProvider time) : IPrincipalRoles
{
    public async ValueTask<IReadOnlyCollection<Guid>> RolesOfAsync(Principal principal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);

        if (principal.Kind == PrincipalKind.ApiKey)
        {
            var key = ApiKeyAuthentication.Verified(http.HttpContext, principal.Id) ?? await keys.ActiveAsync(principal.Id, cancellationToken);
            return key is not null && key.IsActive(time.GetUtcNow()) ? [key.RoleId] : [];
        }

        if (principal.Kind == PrincipalKind.Member)
        {
            return [];
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
