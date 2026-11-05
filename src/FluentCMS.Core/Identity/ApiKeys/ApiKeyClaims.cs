using System.Globalization;
using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;

namespace FluentCMS.Core.Identity.ApiKeys;

// What a key is to the rest of the engine: the claims the handler puts on the request, and the Principal they become.
internal static class ApiKeyClaims
{
    public static List<KeyValuePair<string, string>> Of(ApiKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        List<KeyValuePair<string, string>> claims =
        [
            new(CmsClaimTypes.Subject, key.Id.ToString("D")),
            new(CmsClaimTypes.Name, key.Name),
            new(CmsClaimTypes.Kind, nameof(PrincipalKind.ApiKey)),
            new(CmsClaimTypes.Roles, key.RoleId.ToString("D")),
            new(CmsClaimTypes.Scope, ApiKeyFormat.Scope(key.Class)),
        ];

        if (SitesOf(key) is { } sites)
        {
            claims.Add(new(CmsClaimTypes.Sites, string.Join(' ', sites.Select(site => site.ToString("D")))));
        }

        if (TypesOf(key) is { } types)
        {
            claims.Add(new(CmsClaimTypes.Types, string.Join(' ', types)));
        }

        if (key.RateLimit is { } limit)
        {
            claims.Add(new(CmsClaimTypes.RateLimit, limit.ToString(CultureInfo.InvariantCulture)));
        }

        return claims;
    }

    public static Principal PrincipalOf(ApiKey key) =>
        new(PrincipalKind.ApiKey, key.Id, key.Name, Of(key).Where(claim => claim.Key is not (CmsClaimTypes.Subject or CmsClaimTypes.Name or CmsClaimTypes.Kind)));

    public static IReadOnlyList<Guid>? SitesOf(ApiKey key) =>
        key.SiteIds is null ? null : JsonSerializer.Deserialize<List<Guid>>(key.SiteIds) ?? [];

    public static IReadOnlyList<string>? TypesOf(ApiKey key) =>
        key.TypeNames is null ? null : JsonSerializer.Deserialize<List<string>>(key.TypeNames) ?? [];
}
