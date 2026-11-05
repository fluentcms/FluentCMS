using FluentCMS.Content;
using FluentCMS.Core.Storage;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Identity.ApiKeys;

// A key as it is now, for what checks a key again after the request that presented it: its roles on every evaluation, and
// its whole binding when a scheduled publish runs. A revoked or expired key, or one that is gone, is no principal. It reads
// through CmsDbContext, so it sees the row inside the transaction of a scheduled publish.
internal sealed class ApiKeyPrincipals(CmsDbContext context, TimeProvider time)
{
    public async Task<ApiKey?> ActiveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var key = await context.Set<ApiKey>().AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        return key is not null && key.IsActive(time.GetUtcNow()) ? key : null;
    }

    public async Task<Principal?> CurrentAsync(Guid id, CancellationToken cancellationToken = default) =>
        await ActiveAsync(id, cancellationToken) is { } key ? ApiKeyClaims.PrincipalOf(key) : null;
}
