using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FluentCMS.Admin.Api;

namespace FluentCMS.Plugins.MultiSite.Admin.Api;

// A site as GET /sites lists it and POST /sites answers it.
internal sealed record SiteSummary
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required string Slug { get; init; }

    public required string DefaultLocale { get; init; }

    public bool IsDefault { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

// A host name of a site; a site with host names has exactly one primary host.
internal sealed record SiteHost
{
    public required Guid SiteId { get; init; }

    public required string Hostname { get; init; }

    public bool IsPrimary { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

// The calls of the half (spec 5.6), each of which needs sites.manage for the instance. They go to the site the shell shows,
// where the plugin is enabled, since the server answers the plugin's routes only there; none is about content, so none
// sends a content locale.
internal static class MultiSiteApi
{
    // The default site first.
    public static async Task<IReadOnlyList<SiteSummary>> GetSitesAsync(this IManagementApi api, CancellationToken cancellationToken) =>
        (await api.SendAsync(Request(HttpMethod.Get, "sites"), MultiSiteJsonContext.Default.ItemListSiteSummary, cancellationToken)).Value.Items;

    public static async Task<SiteSummary> GetSiteAsync(this IManagementApi api, Guid id, CancellationToken cancellationToken) =>
        (await api.SendAsync(Request(HttpMethod.Get, Site(id)), MultiSiteJsonContext.Default.SiteSummary, cancellationToken)).Value;

    public static async Task<SiteSummary> CreateSiteAsync(this IManagementApi api, string name, string slug, string defaultLocale, CancellationToken cancellationToken)
    {
        var body = new JsonObject { ["name"] = name, ["slug"] = slug, ["defaultLocale"] = defaultLocale };
        return (await api.SendAsync(Request(HttpMethod.Post, "sites", body), MultiSiteJsonContext.Default.SiteSummary, cancellationToken)).Value;
    }

    public static Task DeleteSiteAsync(this IManagementApi api, Guid id, CancellationToken cancellationToken) =>
        api.SendAsync(Request(HttpMethod.Delete, Site(id)), cancellationToken);

    // The primary host first.
    public static async Task<IReadOnlyList<SiteHost>> GetHostsAsync(this IManagementApi api, Guid id, CancellationToken cancellationToken) =>
        (await api.SendAsync(Request(HttpMethod.Get, Site(id) + "/hosts"), MultiSiteJsonContext.Default.ItemListSiteHost, cancellationToken)).Value.Items;

    // The first host of a site is its primary host whatever isPrimary says.
    public static async Task<SiteHost> AddHostAsync(this IManagementApi api, Guid id, string hostname, bool primary, CancellationToken cancellationToken)
    {
        var body = new JsonObject { ["hostname"] = hostname, ["isPrimary"] = primary };
        return (await api.SendAsync(Request(HttpMethod.Post, Site(id) + "/hosts", body), MultiSiteJsonContext.Default.SiteHost, cancellationToken)).Value;
    }

    public static async Task<SiteHost> MakePrimaryAsync(this IManagementApi api, Guid id, string hostname, CancellationToken cancellationToken) =>
        (await api.SendAsync(Request(HttpMethod.Patch, Host(id, hostname), new JsonObject { ["isPrimary"] = true }), MultiSiteJsonContext.Default.SiteHost, cancellationToken)).Value;

    public static Task RemoveHostAsync(this IManagementApi api, Guid id, string hostname, CancellationToken cancellationToken) =>
        api.SendAsync(Request(HttpMethod.Delete, Host(id, hostname)), cancellationToken);

    private static string Site(Guid id) => $"sites/{id:D}";

    private static string Host(Guid id, string hostname) => $"{Site(id)}/hosts/{Uri.EscapeDataString(hostname)}";

    private static ApiRequest Request(HttpMethod method, string path, JsonNode? body = null) => new(method, path) { Body = body, NoLocale = true };
}

// The answers of the routes the pages tell apart, by code and path only.
internal static class MultiSiteProblems
{
    public const string SiteNotFound = "FCMS2301";
    public const string DefaultSiteProtected = "FCMS2302";
    public const string SiteHasContent = "FCMS2303";

    // A name of 1 to 128 characters, or a slug of lower case letters and digits joined by hyphens that no site has.
    public const string InvalidSite = "FCMS2304";
    public const string InvalidLocale = "FCMS2311";
    public const string InvalidHostName = "FCMS1660";
    public const string WildcardHostName = "FCMS1661";
    public const string HostNameWithPort = "FCMS1662";
    public const string HostNameTaken = "FCMS1663";
    public const string HostNotFound = "FCMS1664";
    public const string PrimaryHostNeeded = "FCMS1665";
    public const string HostNamesBusy = "FCMS1666";

    // What a call may throw: a problem, or a server that did not answer or answered nonsense.
    public static bool IsFailure(Exception exception) =>
        exception is ApiProblemException or HttpRequestException or TaskCanceledException or JsonException;

    public static bool Has(Exception exception, string code) =>
        exception is ApiProblemException { Problem: var problem }
        && (problem.Code == code || problem.Errors.Any(error => error.Code == code));

    public static bool Has(Exception exception, string code, string path) =>
        exception is ApiProblemException { Problem: var problem } && problem.Errors.Any(error => error.Code == code && error.Path == path);

    // The site the path names is gone.
    public static bool TargetGone(Exception exception) => Has(exception, SiteNotFound, "site");

    // The site the shell sends as X-Cms-Site is gone, which the server's site resolver says before any route answers.
    public static bool ShellSiteGone(Exception exception) => Has(exception, SiteNotFound, "X-Cms-Site");
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(SiteSummary))]
[JsonSerializable(typeof(SiteHost))]
[JsonSerializable(typeof(ItemList<SiteSummary>))]
[JsonSerializable(typeof(ItemList<SiteHost>))]
internal sealed partial class MultiSiteJsonContext : JsonSerializerContext;
