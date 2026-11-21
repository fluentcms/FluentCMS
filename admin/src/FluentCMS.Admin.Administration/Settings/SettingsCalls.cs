using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FluentCMS.Admin.Api;

namespace FluentCMS.Admin.Administration.Settings;

// An area of GET /settings: whether the caller may manage it in the site of the request, at /settings/{area}, and for the
// instance defaults, at /settings/{area}/defaults. An area it may manage in neither is not listed.
internal sealed record SettingsAreaInfo
{
    public required string Area { get; init; }

    // The plugin that registers the area; null for an area of core or of the host.
    public string? Plugin { get; init; }

    public bool Site { get; init; }

    public bool Defaults { get; init; }
}

// The read of an area: the document without its secret keys, where it comes from (site, default or code), the version a
// write or a reset names as If-Match (0 when the scope has no row), the secret keys that hold a readable value, those the
// site inherits from the defaults, and the JSON schema of the area.
internal sealed record SettingsRead
{
    public required string Area { get; init; }

    public Guid? SiteId { get; init; }

    public required string Source { get; init; }

    public int Version { get; init; }

    public required JsonObject Value { get; init; }

    public IReadOnlyList<string> SecretsSet { get; init; } = [];

    public IReadOnlyList<string> SecretsInherited { get; init; } = [];

    public required JsonObject Schema { get; init; }
}

// Where an area is read and written: the site of the request, or the instance defaults. Site is the site the form read in,
// which its writes name, as the content form's do, so that a write never goes to a site the session switched to since.
internal sealed record SettingsScope(string Area, bool Defaults, string? Site = null)
{
    public string Path => $"settings/{Uri.EscapeDataString(Area)}" + (Defaults ? "/defaults" : "");
}

// The settings routes of the Management API (spec 4.16). None is about content, so no request sends a content locale.
internal static class SettingsCalls
{
    public const string SourceSite = "site";
    public const string SourceDefault = "default";
    public const string SourceCode = "code";

    // Areas read from the instance defaults only, so that a value a site sets changes nothing: core's branding, which the
    // hosted pages read, and the MCP plugin's registration policy. GET /settings does not tell them yet, so they are known
    // by name, and the menu offers their defaults alone.
    private static readonly HashSet<string> DefaultsOnly = new(["branding", "mcp"], StringComparer.Ordinal);

    public static bool IsDefaultsOnly(string area) => DefaultsOnly.Contains(area);

    // The address of the page the menu links an area to: the site's values when the caller may manage them, else the
    // defaults; none for an area read from the defaults that the caller may manage in the site only.
    public static string? AddressOf(SettingsAreaInfo area)
    {
        ArgumentNullException.ThrowIfNull(area);
        var path = $"settings/{Uri.EscapeDataString(area.Area)}";
        return area.Site && !IsDefaultsOnly(area.Area) ? path : area.Defaults ? path + "/defaults" : null;
    }

    public static async Task<IReadOnlyList<SettingsAreaInfo>> GetSettingsAreasAsync(this IManagementApi api, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(api);
        return (await api.SendAsync(new ApiRequest(HttpMethod.Get, "settings") { NoLocale = true }, SettingsJsonContext.Default.ItemListSettingsAreaInfo, cancellationToken)).Value.Items;
    }

    public static async Task<SettingsRead> GetSettingsAsync(this IManagementApi api, SettingsScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(scope);
        return (await api.SendAsync(new ApiRequest(HttpMethod.Get, scope.Path) { Site = scope.Site, NoLocale = true }, SettingsJsonContext.Default.SettingsRead, cancellationToken)).Value;
    }

    // The whole document over the version it was read at. A secret key it leaves out keeps what is stored, so the caller
    // names only the secrets the user typed.
    public static async Task<SettingsRead> PutSettingsAsync(this IManagementApi api, SettingsScope scope, JsonObject document, int version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(scope);
        var request = new ApiRequest(HttpMethod.Put, scope.Path) { Body = document, IfMatch = Tag(version), Site = scope.Site, NoLocale = true };
        return (await api.SendAsync(request, SettingsJsonContext.Default.SettingsRead, cancellationToken)).Value;
    }

    // The site back to the defaults, or the defaults back to the code's, over the version it was read at.
    public static async Task ResetSettingsAsync(this IManagementApi api, SettingsScope scope, int version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(scope);
        await api.SendAsync(new ApiRequest(HttpMethod.Delete, scope.Path) { IfMatch = Tag(version), Site = scope.Site, NoLocale = true }, cancellationToken);
    }

    private static string Tag(int version) => version.ToString(CultureInfo.InvariantCulture);
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ItemList<SettingsAreaInfo>))]
[JsonSerializable(typeof(SettingsRead))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
