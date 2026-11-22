using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Identity.ApiKeys;

// A key as it is now, for what checks a key again after the request that presented it: its roles on every evaluation, and
// its whole binding when a scheduled publish runs. A revoked or expired key, or one that is gone, is no principal. It reads
// through CmsDbContext, so it sees the row inside the transaction of a scheduled publish.
internal sealed class ApiKeyPrincipals(CmsDbContext context, IHttpContextAccessor http, TimeProvider time)
{
    public async Task<ApiKey?> ActiveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var key = await context.Set<ApiKey>().AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        return key is not null && key.IsActive(time.GetUtcNow()) ? key : null;
    }

    public async Task<Principal?> CurrentAsync(Guid id, CancellationToken cancellationToken = default) =>
        await ActiveAsync(id, cancellationToken) is { } key ? ApiKeyClaims.PrincipalOf(key) : null;

    // A change made by a key, of keys or invitations, reads the key again in its transaction, since the request holds the row
    // it authenticated (#3391), and refuses it as the authentication handler would now: revoked or expired, a secret a
    // rotation without overlap retired or whose overlap ended, or a client its addresses no longer allow (#3483). A key the
    // handler did not verify for this request, as in a command or a job, has no secret or address to compare.
    public async Task RequireCallerAsync(Principal? caller, CancellationToken cancellationToken = default)
    {
        if (caller is not { Kind: PrincipalKind.ApiKey })
        {
            return;
        }

        var key = await ActiveAsync(caller.Id, cancellationToken);
        var request = http.HttpContext;
        if (key is null
            || (ApiKeyAuthentication.PresentedHash(request, caller.Id) is { } presented
                && (!Takes(key, presented, time.GetUtcNow()) || !ApiKeyAddresses.Allows(key, request!.Connection.RemoteIpAddress))))
        {
            throw new ManagementAccessDeniedException("The key that made this request no longer authenticates it: it was revoked, expired or rotated, or no longer allows this address.");
        }
    }

    private static bool Takes(ApiKey key, string presented, DateTimeOffset now) =>
        string.Equals(presented, key.KeyHash, StringComparison.Ordinal)
        || (string.Equals(presented, key.PreviousKeyHash, StringComparison.Ordinal) && key.PreviousKeyExpiresAt > now);
}
