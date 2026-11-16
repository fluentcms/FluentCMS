namespace FluentCMS.Admin.Api;

/// <summary>
/// What the caller may do in the site of the request, from <c>GET /me/access</c>. It shows only the caller's own reach: the
/// types it may read, and no other principal's grants.
/// </summary>
public sealed record MyAccessInfo
{
    /// <summary>The id of the site the access is described in.</summary>
    public required Guid Site { get; init; }

    /// <summary>The locale the access is described in.</summary>
    public required string Locale { get; init; }

    /// <summary>The permissions that are not per type which the caller holds in the site, with no limit to resources.</summary>
    public required IReadOnlyList<string> Permissions { get; init; }

    /// <summary>The permissions that are not per type which the caller holds for the instance.</summary>
    public required IReadOnlyList<string> InstancePermissions { get; init; }

    /// <summary>The permissions the caller holds in the site only for some resources, which a call checks one by one.</summary>
    public required IReadOnlyList<string> LimitedPermissions { get; init; }

    /// <summary>Whether the caller may search content.</summary>
    public required bool Search { get; init; }

    /// <summary>What the caller may do with each type it may read, keyed by the type's name.</summary>
    public required IReadOnlyDictionary<string, TypeAccessInfo> Types { get; init; }
}
