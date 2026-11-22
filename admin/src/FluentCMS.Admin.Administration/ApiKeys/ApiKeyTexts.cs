using System.Text.Json;
using FluentCMS.Admin.Administration.Common;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Localization;
using Microsoft.Extensions.Localization;

namespace FluentCMS.Admin.Administration.ApiKeys;

internal static class ApiKeyRoutes
{
    public const string List = "api-keys";

    public static string Key(Guid id) => $"api-keys/{id:D}";
}

// How the keys pages name a key's role, sites and state.
internal static class ApiKeyTexts
{
    // The roles the caller may read, or none: a caller who manages keys may still be refused GET /roles.
    public static async Task<IReadOnlyList<RoleSummary>> RolesAsync(IManagementApi api, CancellationToken cancellationToken)
    {
        try
        {
            return await api.GetRolesAsync(cancellationToken);
        }
        catch (Exception failure) when (failure is ApiProblemException or HttpRequestException or JsonException)
        {
            return [];
        }
    }

    // A role of a site is named with its site, since a name is unique in a site only.
    public static string Role(ApiKeyInfo key, IReadOnlyList<RoleSummary>? roles, IReadOnlyList<MySiteInfo> sites, IStringLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(localizer);
        if (roles?.FirstOrDefault(role => role.Id == key.RoleId) is not { } found)
        {
            return localizer["ApiKeys.Role.Unknown"];
        }

        return found.SiteId is not { } site ? found.Name : localizer["ApiKeys.Role.OfSite", found.Name, SiteName(site, sites, localizer)];
    }

    public static string Sites(ApiKeyInfo key, IReadOnlyList<MySiteInfo> sites, IStringLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(localizer);
        return key.SiteIds is not { } bound
            ? localizer["ApiKeys.Sites.All"]
            : string.Join(", ", bound.Select(site => SiteName(site, sites, localizer)).Distinct(StringComparer.Ordinal));
    }

    public static string Status(ApiKeyInfo key, DateTimeOffset now, FormatService formats, IStringLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentNullException.ThrowIfNull(localizer);
        return key switch
        {
            { RevokedAt: { } revoked } => localizer["ApiKeys.Status.Revoked", formats.DateTime(revoked)],
            { ExpiresAt: { } expires } when expires <= now => localizer["ApiKeys.Status.Expired", formats.DateTime(expires)],
            { PreviousKeyExpiresAt: { } overlap } => localizer["ApiKeys.Status.Overlap", formats.DateTime(overlap)],
            { ExpiresAt: { } expires } => localizer["ApiKeys.Status.Expires", formats.DateTime(expires)],
            _ => localizer["ApiKeys.Status.Active"],
        };
    }

    // The site's name as GET /me/sites gives it; a site the caller has no role in is not named there.
    private static string SiteName(Guid site, IReadOnlyList<MySiteInfo> sites, IStringLocalizer localizer) =>
        sites.FirstOrDefault(item => item.Id == site)?.Name ?? localizer["ApiKeys.Sites.Unknown"];
}

// The allowed addresses as the forms take them, one a line; none is any address.
internal static class ApiKeyAddresses
{
    public static IReadOnlyList<string>? Parse(string? text)
    {
        List<string> lines = [.. (text ?? "").Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0)];
        return lines.Count == 0 ? null : lines;
    }

    public static string Format(IReadOnlyList<string>? addresses) => addresses is null ? "" : string.Join('\n', addresses);
}
