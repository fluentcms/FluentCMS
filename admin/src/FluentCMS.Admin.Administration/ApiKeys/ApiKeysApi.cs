using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FluentCMS.Admin.Api;

namespace FluentCMS.Admin.Administration.ApiKeys;

// A key as GET /api-keys gives it. Null sites, types or addresses is no binding: every site, every type, any address.
internal sealed record ApiKeyInfo
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    // delivery, preview or management.
    public required string Class { get; init; }

    public required string KeyPrefix { get; init; }

    public required Guid RoleId { get; init; }

    public IReadOnlyList<Guid>? SiteIds { get; init; }

    public IReadOnlyList<string>? TypeNames { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public DateTimeOffset? LastUsedAt { get; init; }

    public int? RateLimit { get; init; }

    public DateTimeOffset? RevokedAt { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public IReadOnlyList<string>? AllowedAddresses { get; init; }

    // While the secret a rotation replaced is still accepted.
    public DateTimeOffset? PreviousKeyExpiresAt { get; init; }

    public bool Revoked => RevokedAt is not null;

    public bool Expired(DateTimeOffset now) => ExpiresAt <= now;
}

// What a create or a rotation answers that the admin reads: the key's id and its secret, which no other answer has. The secret
// is kept out of ToString, so that nothing that prints the record shows it.
internal sealed record ApiKeySecret
{
    public required Guid Id { get; init; }

    public required string Secret { get; init; }

    public override string ToString() => $"{nameof(ApiKeySecret)} {{ Id = {Id} }}";
}

// The fields of a new key; a list left null is no binding.
internal sealed record ApiKeyDraft(string Name, string Class, Guid RoleId)
{
    public IReadOnlyList<Guid>? SiteIds { get; init; }

    public IReadOnlyList<string>? TypeNames { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public int? RateLimit { get; init; }

    public IReadOnlyList<string>? AllowedAddresses { get; init; }
}

// The calls of the keys pages (spec 4.15). A key the caller does not manage answers 404 (FCMS3015); a caller who may manage
// no key in the site, or whose roles lack a grant of the key's role as bound, is answered 403. Create sends no
// Idempotency-Key: the server does not take one on this route, and a store that kept the answer would keep the secret.
internal static class ApiKeysApi
{
    public static async Task<IReadOnlyList<ApiKeyInfo>> GetApiKeysAsync(this IManagementApi api, CancellationToken cancellationToken) =>
        (await api.SendAsync(new ApiRequest(HttpMethod.Get, "api-keys"), ApiKeysJsonContext.Default.ItemListApiKeyInfo, cancellationToken)).Value.Items;

    public static async Task<ApiKeyInfo> GetApiKeyAsync(this IManagementApi api, Guid id, CancellationToken cancellationToken) =>
        (await api.SendAsync(new ApiRequest(HttpMethod.Get, Path(id)), ApiKeysJsonContext.Default.ApiKeyInfo, cancellationToken)).Value;

    public static async Task<ApiKeySecret> CreateApiKeyAsync(this IManagementApi api, ApiKeyDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var body = new JsonObject
        {
            ["name"] = draft.Name,
            ["class"] = draft.Class,
            ["roleId"] = draft.RoleId.ToString("D"),
        };
        if (draft.SiteIds is { } sites)
        {
            body["siteIds"] = new JsonArray([.. sites.Select(site => (JsonNode)site.ToString("D"))]);
        }

        if (draft.TypeNames is { } types)
        {
            body["typeNames"] = Strings(types);
        }

        if (draft.ExpiresAt is { } expires)
        {
            body["expiresAt"] = Moment(expires);
        }

        if (draft.RateLimit is { } limit)
        {
            body["rateLimit"] = limit;
        }

        if (draft.AllowedAddresses is { } addresses)
        {
            body["allowedAddresses"] = Strings(addresses);
        }

        return (await api.SendAsync(new ApiRequest(HttpMethod.Post, "api-keys") { Body = body }, ApiKeysJsonContext.Default.ApiKeySecret, cancellationToken)).Value;
    }

    // Without an overlap, the server's own, which is the longest it allows.
    public static async Task<ApiKeySecret> RotateApiKeyAsync(this IManagementApi api, Guid id, int? overlapSeconds, CancellationToken cancellationToken)
    {
        var body = new JsonObject();
        if (overlapSeconds is { } seconds)
        {
            body["overlapSeconds"] = seconds;
        }

        return (await api.SendAsync(new ApiRequest(HttpMethod.Post, Path(id) + "/rotate") { Body = body }, ApiKeysJsonContext.Default.ApiKeySecret, cancellationToken)).Value;
    }

    // Without a moment, now.
    public static async Task<ApiKeyInfo> ExpireApiKeyAsync(this IManagementApi api, Guid id, DateTimeOffset? at, CancellationToken cancellationToken)
    {
        var body = new JsonObject();
        if (at is { } moment)
        {
            body["at"] = Moment(moment);
        }

        return (await api.SendAsync(new ApiRequest(HttpMethod.Post, Path(id) + "/expire") { Body = body }, ApiKeysJsonContext.Default.ApiKeyInfo, cancellationToken)).Value;
    }

    public static Task RevokeApiKeyAsync(this IManagementApi api, Guid id, CancellationToken cancellationToken) =>
        api.SendAsync(new ApiRequest(HttpMethod.Delete, Path(id)), cancellationToken);

    // Null lets the key in from any address.
    public static async Task<ApiKeyInfo> SetApiKeyAddressesAsync(this IManagementApi api, Guid id, IReadOnlyList<string>? addresses, CancellationToken cancellationToken)
    {
        var body = new JsonObject { ["allowedAddresses"] = addresses is null ? null : Strings(addresses) };
        return (await api.SendAsync(new ApiRequest(HttpMethod.Put, Path(id) + "/allowed-addresses") { Body = body }, ApiKeysJsonContext.Default.ApiKeyInfo, cancellationToken)).Value;
    }

    private static string Path(Guid id) => $"api-keys/{id:D}";

    private static JsonArray Strings(IEnumerable<string> values) => new([.. values.Select(value => (JsonNode)value)]);

    private static string Moment(DateTimeOffset value) => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ItemList<ApiKeyInfo>))]
[JsonSerializable(typeof(ApiKeyInfo))]
[JsonSerializable(typeof(ApiKeySecret))]
internal sealed partial class ApiKeysJsonContext : JsonSerializerContext;
