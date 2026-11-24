using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FluentCMS.Admin.Api;

namespace FluentCMS.Admin.Administration.Sites;

// A site as GET and PATCH /sites/{id} answer it.
internal sealed record SiteRecord
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required string Slug { get; init; }

    public required string DefaultLocale { get; init; }

    public bool IsDefault { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

// A locale of a site. Null fallbacks are the implicit chain; an empty list falls back to nothing.
internal sealed record SiteLocaleRecord
{
    public required string Code { get; init; }

    public int Position { get; init; }

    public IReadOnlyList<string>? Fallbacks { get; init; }

    public bool Enabled { get; init; }
}

// The calls of the sites area (spec 4.16), each for the site its path names, which needs sites.manage there. None is about
// content, so none sends a content locale: the server refuses one the site does not allow, as one the page just disabled.
internal static class SitesApi
{
    public static async Task<SiteRecord> GetSiteAsync(this IManagementApi api, Guid id, CancellationToken cancellationToken) =>
        (await api.SendAsync(Request(HttpMethod.Get, id, ""), SitesJsonContext.Default.SiteRecord, cancellationToken)).Value;

    // A member left null is left as it is.
    public static async Task<SiteRecord> ChangeSiteAsync(this IManagementApi api, Guid id, string? name, string? defaultLocale, CancellationToken cancellationToken)
    {
        var body = new JsonObject();
        if (name is not null)
        {
            body["name"] = name;
        }

        if (defaultLocale is not null)
        {
            body["defaultLocale"] = defaultLocale;
        }

        return (await api.SendAsync(Request(HttpMethod.Patch, id, "", body), SitesJsonContext.Default.SiteRecord, cancellationToken)).Value;
    }

    public static async Task<IReadOnlyList<SiteLocaleRecord>> GetLocalesAsync(this IManagementApi api, Guid id, CancellationToken cancellationToken) =>
        (await api.SendAsync(Request(HttpMethod.Get, id, "/locales"), SitesJsonContext.Default.ItemListSiteLocaleRecord, cancellationToken)).Value.Items;

    // After the others; null fallbacks are the implicit chain.
    public static async Task<SiteLocaleRecord> AddLocaleAsync(this IManagementApi api, Guid id, string code, bool enabled, IReadOnlyList<string>? fallbacks, CancellationToken cancellationToken)
    {
        var body = new JsonObject { ["code"] = code, ["enabled"] = enabled };
        if (fallbacks is not null)
        {
            body["fallbacks"] = Codes(fallbacks);
        }

        return (await api.SendAsync(Request(HttpMethod.Post, id, "/locales", body), SitesJsonContext.Default.SiteLocaleRecord, cancellationToken)).Value;
    }

    public static Task<SiteLocaleRecord> MoveLocaleAsync(this IManagementApi api, Guid id, string code, int position, CancellationToken cancellationToken) =>
        api.ChangeLocaleAsync(id, code, new JsonObject { ["position"] = position }, cancellationToken);

    public static Task<SiteLocaleRecord> EnableLocaleAsync(this IManagementApi api, Guid id, string code, bool enabled, CancellationToken cancellationToken) =>
        api.ChangeLocaleAsync(id, code, new JsonObject { ["enabled"] = enabled }, cancellationToken);

    // Null goes back to the implicit chain, which the member sent as null asks for.
    public static Task<SiteLocaleRecord> SetFallbacksAsync(this IManagementApi api, Guid id, string code, IReadOnlyList<string>? fallbacks, CancellationToken cancellationToken) =>
        api.ChangeLocaleAsync(id, code, new JsonObject { ["fallbacks"] = fallbacks is null ? null : Codes(fallbacks) }, cancellationToken);

    public static Task RemoveLocaleAsync(this IManagementApi api, Guid id, string code, CancellationToken cancellationToken) =>
        api.SendAsync(Request(HttpMethod.Delete, id, Locale(code)), cancellationToken);

    private static async Task<SiteLocaleRecord> ChangeLocaleAsync(this IManagementApi api, Guid id, string code, JsonObject body, CancellationToken cancellationToken) =>
        (await api.SendAsync(Request(HttpMethod.Patch, id, Locale(code), body), SitesJsonContext.Default.SiteLocaleRecord, cancellationToken)).Value;

    private static string Locale(string code) => "/locales/" + Uri.EscapeDataString(code);

    private static JsonArray Codes(IReadOnlyList<string> codes) => new([.. codes.Select(code => (JsonNode)code)]);

    // The request names the site of its path, and so sends no locale.
    private static ApiRequest Request(HttpMethod method, Guid id, string rest, JsonNode? body = null) =>
        new(method, $"sites/{id:D}{rest}") { Body = body, Site = id.ToString("D") };
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(SiteRecord))]
[JsonSerializable(typeof(SiteLocaleRecord))]
[JsonSerializable(typeof(ItemList<SiteLocaleRecord>))]
internal sealed partial class SitesJsonContext : JsonSerializerContext;
