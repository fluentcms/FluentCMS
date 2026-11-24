using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FluentCMS.Admin.Api;

namespace FluentCMS.Admin.Administration.Plugins;

// The calls of the plugins area (spec 4.15). The list is the one the shell reads, which every caller may read, with the
// enablement in each site where the caller holds plugins.manage; a switch needs plugins.manage in the site it switches.
// Neither is about content, so neither sends a content locale.
internal static class PluginsApi
{
    public static async Task<IReadOnlyList<PluginInfo>> GetPluginsAsync(this IManagementApi api, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(api);
        return (await api.SendAsync(new ApiRequest(HttpMethod.Get, "plugins") { NoLocale = true }, PluginsJsonContext.Default.ItemListPluginInfo, cancellationToken)).Value.Items;
    }

    public static async Task<PluginSiteInfo> SetPluginEnabledAsync(this IManagementApi api, string plugin, Guid site, bool enabled, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(api);
        var request = new ApiRequest(HttpMethod.Put, $"plugins/{Uri.EscapeDataString(plugin)}/sites/{site:D}") { Body = new JsonObject { ["enabled"] = enabled }, NoLocale = true };
        return (await api.SendAsync(request, PluginsJsonContext.Default.PluginSiteInfo, cancellationToken)).Value;
    }
}

// The answers of the routes the page tells apart, by status, code and path only (spec 2.7).
internal static class PluginProblems
{
    public const string PluginNotFound = "FCMS1551";
    public const string SiteNotFound = "FCMS2301";

    public static bool IsFailure(Exception exception) =>
        exception is ApiProblemException or HttpRequestException or TaskCanceledException or JsonException;

    public static bool Has(Exception exception, string code) =>
        exception is ApiProblemException { Problem: var problem } && (problem.Code == code || problem.Errors.Any(error => error.Code == code));

    public static bool IsForbidden(Exception exception) => exception is ApiProblemException { Problem.Status: 403 };
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ItemList<PluginInfo>))]
[JsonSerializable(typeof(PluginSiteInfo))]
internal sealed partial class PluginsJsonContext : JsonSerializerContext;
