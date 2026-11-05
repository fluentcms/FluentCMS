namespace FluentCMS.Core.Identity.ApiKeys;

internal enum ApiKeyClass
{
    Delivery = 1,

    Preview = 2,

    Management = 3,
}

// A row of cms_api_key. The key itself is shown once, when it is created; the row keeps its SHA-256 and the start of it,
// which finds the row without reading the hash of every key.
internal sealed class ApiKey
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public ApiKeyClass Class { get; set; }

    public string KeyPrefix { get; set; } = "";

    public string KeyHash { get; set; } = "";

    public Guid RoleId { get; set; }

    // JSON arrays; null when the key is not bound to sites, or to types.
    public string? SiteIds { get; set; }

    public string? TypeNames { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }

    // Requests per minute; null for the policy's own limit.
    public int? RateLimit { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid CreatedBy { get; set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && (ExpiresAt is not { } expires || expires > now);
}
