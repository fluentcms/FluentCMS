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
    private static readonly object ReadItem = new();

    public async ValueTask<IReadOnlyCollection<Guid>> RolesOfAsync(Principal principal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);

        if (principal.Kind == PrincipalKind.ApiKey)
        {
            var key = await KeyAsync(principal, cancellationToken);
            return key is not null ? [key.RoleId] : [];
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

    // Only an API key is bound to types, and one that is revoked, expired or gone binds to none of them. A user and a service
    // account are bound by their roles alone. A member mints no preview token, and its claim stays what it is.
    public async ValueTask<IReadOnlySet<string>?> TypesOfAsync(Principal principal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return principal.Kind switch
        {
            PrincipalKind.ApiKey => await KeyAsync(principal, cancellationToken) is { } key
                ? ApiKeyClaims.TypesOf(key)?.ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(),
            PrincipalKind.AdminUser or PrincipalKind.ServiceAccount => null,
            _ => PrincipalBinding.Of(principal).Types,
        };
    }

    // A key that no handler verified, such as the one that minted a preview token, is read once for the request, since its
    // role and its types are both asked for; outside a request it is read each time.
    private async ValueTask<ApiKey?> KeyAsync(Principal principal, CancellationToken cancellationToken)
    {
        var request = http.HttpContext;
        var key = ApiKeyAuthentication.Verified(request, principal.Id);
        if (key is null && request is not null && request.Items.TryGetValue(ReadItem, out var item) && item is KeyRead read && read.Id == principal.Id)
        {
            key = read.Key;
        }
        else if (key is null)
        {
            key = await keys.ActiveAsync(principal.Id, cancellationToken);
            if (request is not null)
            {
                request.Items[ReadItem] = new KeyRead(principal.Id, key);
            }
        }

        return key is not null && key.IsActive(time.GetUtcNow()) ? key : null;
    }

    private sealed record KeyRead(Guid Id, ApiKey? Key);
}
