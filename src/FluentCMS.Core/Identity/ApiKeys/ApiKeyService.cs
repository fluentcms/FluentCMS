using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Events;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Identity.ApiKeys;

// Null sites or types is no binding. An empty list is refused rather than read as either: a client that sends [] may mean
// no site at all, and getting every site instead would widen the key.
internal sealed record ApiKeyRequest(string Name, ApiKeyClass Class, Guid RoleId)
{
    public IReadOnlyCollection<Guid>? SiteIds { get; init; }

    public IReadOnlyCollection<string>? TypeNames { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public int? RateLimit { get; init; }
}

internal sealed record ApiKeyInfo(
    Guid Id,
    string Name,
    ApiKeyClass Class,
    string KeyPrefix,
    Guid RoleId,
    IReadOnlyList<Guid>? SiteIds,
    IReadOnlyList<string>? TypeNames,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastUsedAt,
    int? RateLimit,
    DateTimeOffset? RevokedAt,
    DateTimeOffset CreatedAt,
    Guid CreatedBy)
{
    public static ApiKeyInfo From(ApiKey key) =>
        new(key.Id, key.Name, key.Class, key.KeyPrefix, key.RoleId, ApiKeyClaims.SitesOf(key), ApiKeyClaims.TypesOf(key), key.ExpiresAt, key.LastUsedAt, key.RateLimit, key.RevokedAt, key.CreatedAt, key.CreatedBy);
}

// The only place the secret is, and only on the call that created it. It is kept out of ToString, so a log of this cannot show it.
internal sealed record CreatedApiKey(ApiKeyInfo Key, string Secret)
{
    public override string ToString() => $"CreatedApiKey {{ Key = {Key} }}";
}

// The key a request presented, or why it is refused: for the server's log only, with the id of a key that was found.
internal sealed record ApiKeyCheck(ApiKey? Key, string? Failure, Guid? RefusedKeyId = null);

// A key is managed with apikeys.manage where it reaches: for the instance when it is bound to no site, and in each of its
// sites otherwise, so a caller limited to some sites, a key bound to them included, never makes or touches a key that reaches
// further. A new key's role, as its binding narrows it, holds nothing the caller does not hold.
internal sealed class ApiKeyService(
    SystemDbContext context,
    IdentityChanges changes,
    SchemaModel schema,
    IOperationContextAccessor accessor,
    ManagementAuthority authority,
    RoleService roles,
    TimeProvider time)
{
    // last_used_at says whether a key is still in use, which needs minutes, not every request.
    public static readonly TimeSpan LastUsedInterval = TimeSpan.FromMinutes(5);

    private const int NameLength = 128;

    public async Task<CreatedApiKey> CreateAsync(ApiKeyRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Class))
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Class, "Unknown key class.");
        }

        var now = time.GetUtcNow();
        var name = (request.Name ?? "").Trim();
        List<Guid>? sites = request.SiteIds is { } siteIds ? [.. siteIds.Distinct()] : null;
        List<string>? types = request.TypeNames is { } typeNames ? [.. typeNames.Distinct(StringComparer.Ordinal)] : null;

        List<ValidationError> errors = [];
        if (name.Length == 0 || name.Length > NameLength)
        {
            errors.Add(new ValidationError("name", ApiKeyErrorCodes.InvalidName, $"A key has a name of 1 to {NameLength} characters."));
        }

        if (!await context.Set<Role>().AnyAsync(role => role.Id == request.RoleId, cancellationToken))
        {
            errors.Add(new ValidationError("roleId", IdentityErrorCodes.UnknownRole, "A key is bound to a role that exists."));
        }

        if (sites is { Count: 0 })
        {
            errors.Add(new ValidationError("siteIds", ApiKeyErrorCodes.EmptyBinding, "A key is bound to at least one site, or to none by leaving siteIds out."));
        }
        else if (sites is not null && await context.Set<Site>().CountAsync(site => sites.Contains(site.Id), cancellationToken) != sites.Count)
        {
            errors.Add(new ValidationError("siteIds", ApiKeyErrorCodes.UnknownSite, "A key is bound only to sites that exist."));
        }

        if (types is { Count: 0 })
        {
            errors.Add(new ValidationError("typeNames", ApiKeyErrorCodes.EmptyBinding, "A key is bound to at least one type, or to none by leaving typeNames out."));
        }
        else if (types?.FirstOrDefault(type => schema.FindContentType(type) is null) is { } unknown)
        {
            errors.Add(new ValidationError("typeNames", ApiKeyErrorCodes.UnknownType, $"There is no content type '{unknown}'."));
        }

        if (request.ExpiresAt is { } expires && expires <= now)
        {
            errors.Add(new ValidationError("expiresAt", ApiKeyErrorCodes.InvalidExpiry, "A key expires in the future."));
        }

        if (request.RateLimit is < 1)
        {
            errors.Add(new ValidationError("rateLimit", ApiKeyErrorCodes.InvalidRateLimit, "A rate limit is at least one request a minute."));
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        await RequireManageAsync(sites, cancellationToken);
        await roles.RequireKeyRoleAsync(request.RoleId, sites, types, cancellationToken);

        var secret = ApiKeyFormat.Generate(request.Class);
        var key = new ApiKey
        {
            Id = Guid.CreateVersion7(),
            Name = name,
            Class = request.Class,
            KeyPrefix = ApiKeyFormat.LookupPrefix(secret),
            KeyHash = ApiKeyFormat.Hash(secret),
            RoleId = request.RoleId,
            SiteIds = sites is null ? null : JsonSerializer.Serialize(sites),
            TypeNames = types is null ? null : JsonSerializer.Serialize(types),
            ExpiresAt = request.ExpiresAt,
            RateLimit = request.RateLimit,
            CreatedAt = now,
            CreatedBy = (accessor.Current?.AuditPrincipal ?? Principal.System).Id,
        };

        await changes.RunAsync(
            async () =>
            {
                context.Add(key);
                await context.SaveChangesAsync(cancellationToken);
                await changes.RaiseAsync(new ApiKeyCreatedEvent(key.Id, key.Name, ApiKeyFormat.Scope(key.Class)), cancellationToken);
                return true;
            },
            cancellationToken);

        return new CreatedApiKey(ApiKeyInfo.From(key), secret);
    }

    // The keys the caller manages. One with no permission for any of them is refused rather than shown an empty list.
    public async Task<IReadOnlyList<ApiKeyInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        var keys = await context.Set<ApiKey>().AsNoTracking().OrderBy(key => key.CreatedAt).ThenBy(key => key.Id).ToListAsync(cancellationToken);

        List<ApiKeyInfo> managed = [];
        foreach (var key in keys.Select(ApiKeyInfo.From))
        {
            if (await ManagesAsync(key.SiteIds, cancellationToken))
            {
                managed.Add(key);
            }
        }

        if (managed.Count == 0)
        {
            await RequireAnyAsync(cancellationToken);
        }

        return managed;
    }

    // A key the caller does not manage is not found, so a caller limited to some sites learns nothing of the others.
    public async Task<ApiKeyInfo?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await RequireAnyAsync(cancellationToken);
        return await context.Set<ApiKey>().AsNoTracking().FirstOrDefaultAsync(key => key.Id == id, cancellationToken) is { } key
            && await ManagesAsync(ApiKeyClaims.SitesOf(key), cancellationToken)
                ? ApiKeyInfo.From(key)
                : null;
    }

    // False when the key was revoked already, which raises no second event.
    public async Task<bool> RevokeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await ManagedAsync(id, cancellationToken);
        return await changes.RunAsync(
            async () =>
            {
                var key = await StoredAsync(id, cancellationToken);
                var revoked = await context.Set<ApiKey>()
                    .Where(row => row.Id == id && row.RevokedAt == null)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RevokedAt, time.GetUtcNow()), cancellationToken);
                if (revoked == 0)
                {
                    return false;
                }

                await changes.RaiseAsync(new ApiKeyRevokedEvent(key.Id, key.Name), cancellationToken);
                return true;
            },
            cancellationToken);
    }

    // Brings the expiry forward, to now when no time is given. A key's life is never made longer, since that would need the
    // same decision as a new key. The rule is in the update, so two changes at once cannot extend it between them.
    public async Task ExpireAsync(Guid id, DateTimeOffset? at = null, CancellationToken cancellationToken = default)
    {
        var expires = at ?? time.GetUtcNow();
        await ManagedAsync(id, cancellationToken);
        await changes.RunAsync(
            async () =>
            {
                var key = await StoredAsync(id, cancellationToken);
                var changed = await context.Set<ApiKey>()
                    .Where(row => row.Id == id && (row.ExpiresAt == null || row.ExpiresAt >= expires))
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ExpiresAt, expires), cancellationToken);
                if (changed == 0)
                {
                    throw new ValidationException([new ValidationError("expiresAt", ApiKeyErrorCodes.InvalidExpiry, "A key's expiry is only brought forward.")]);
                }

                await changes.RaiseAsync(new ApiKeyExpiryChangedEvent(key.Id, key.Name, expires), cancellationToken);
                return true;
            },
            cancellationToken);
    }

    // For the authentication handler. The prefix finds the candidates, and the hash of the presented key is compared in
    // constant time with each, so the time taken does not tell how much of a key was right.
    public async Task<ApiKeyCheck> AuthenticateAsync(string secret, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(secret);

        if (!ApiKeyFormat.IsWellFormed(secret))
        {
            return new ApiKeyCheck(null, ApiKeyErrorCodes.InvalidKey);
        }

        var prefix = ApiKeyFormat.LookupPrefix(secret);
        var candidates = await context.Set<ApiKey>().AsNoTracking().Where(key => key.KeyPrefix == prefix).ToListAsync(cancellationToken);
        ApiKey? found = null;
        foreach (var candidate in candidates)
        {
            if (ApiKeyFormat.Matches(secret, candidate.KeyHash))
            {
                found = candidate;
            }
        }

        var now = time.GetUtcNow();
        if (found is null)
        {
            return new ApiKeyCheck(null, ApiKeyErrorCodes.InvalidKey);
        }

        if (found.RevokedAt is not null)
        {
            return new ApiKeyCheck(null, ApiKeyErrorCodes.Revoked, found.Id);
        }

        if (!found.IsActive(now))
        {
            return new ApiKeyCheck(null, ApiKeyErrorCodes.Expired, found.Id);
        }

        await TouchAsync(found, now, cancellationToken);
        return new ApiKeyCheck(found, null);
    }

    // At most one write per key per interval: the row read says whether one is due, and the condition on the update keeps
    // two instances that both thought so from writing twice.
    private async Task TouchAsync(ApiKey key, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var due = now - LastUsedInterval;
        if (key.LastUsedAt is { } last && last > due)
        {
            return;
        }

        await context.Set<ApiKey>()
            .Where(row => row.Id == key.Id && (row.LastUsedAt == null || row.LastUsedAt <= due))
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.LastUsedAt, now), cancellationToken);
        key.LastUsedAt = now;
    }

    private async Task<ApiKey> StoredAsync(Guid id, CancellationToken cancellationToken) =>
        await context.Set<ApiKey>().AsNoTracking().FirstOrDefaultAsync(key => key.Id == id, cancellationToken) ?? throw NotFound(id);

    // Checked before the transaction of the change. A key out of the reach of a caller who manages keys is not found.
    private async Task ManagedAsync(Guid id, CancellationToken cancellationToken)
    {
        await RequireAnyAsync(cancellationToken);
        if (!await ManagesAsync(ApiKeyClaims.SitesOf(await StoredAsync(id, cancellationToken)), cancellationToken))
        {
            throw NotFound(id);
        }
    }

    private async Task<bool> ManagesAsync(IReadOnlyCollection<Guid>? sites, CancellationToken cancellationToken)
    {
        if (sites is null)
        {
            return await authority.AllowsAsync(CorePermissions.ApiKeysManage, site: null, cancellationToken);
        }

        foreach (var site in sites)
        {
            if (!await authority.AllowsAsync(CorePermissions.ApiKeysManage, site, cancellationToken))
            {
                return false;
            }
        }

        return sites.Count > 0;
    }

    // A caller who manages no key in the site of the request is refused, rather than told a key is not there.
    private async Task RequireAnyAsync(CancellationToken cancellationToken)
    {
        if (!await authority.AllowsAsync(CorePermissions.ApiKeysManage, authority.CurrentSite?.Value, cancellationToken))
        {
            throw new ManagementAccessDeniedException($"API keys are managed with {CorePermissions.ApiKeysManage}.");
        }
    }

    private async Task RequireManageAsync(IReadOnlyCollection<Guid>? sites, CancellationToken cancellationToken)
    {
        if (!await ManagesAsync(sites, cancellationToken))
        {
            throw new ManagementAccessDeniedException(sites is null
                ? $"A key bound to no site needs the instance-wide permission {CorePermissions.ApiKeysManage}."
                : $"A key needs {CorePermissions.ApiKeysManage} in every site it is bound to.");
        }
    }

    private static ValidationException NotFound(Guid id) =>
        new([new ValidationError("id", ApiKeyErrorCodes.NotFound, $"There is no API key {id:D}.")]);
}
