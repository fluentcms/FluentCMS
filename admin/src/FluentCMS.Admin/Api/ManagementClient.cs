using System.Text.Json.Nodes;
using FluentCMS.Admin.Content;

namespace FluentCMS.Admin.Api;

// The typed calls of the core areas (AQ5), over the one pipeline.
internal sealed class ManagementClient(IManagementApi api)
{
    private static ManagementJsonContext Json => ManagementJsonContext.Default;

    public async Task<MeResponse> GetMeAsync(CancellationToken cancellationToken) =>
        (await api.SendAsync(new ApiRequest(HttpMethod.Get, "me"), Json.MeResponse, cancellationToken)).Value;

    public async Task<IReadOnlyList<MySiteInfo>> GetMySitesAsync(CancellationToken cancellationToken) =>
        (await api.SendAsync(new ApiRequest(HttpMethod.Get, "me/sites"), Json.ItemListMySiteInfo, cancellationToken)).Value.Items;

    public async Task<SchemaDescription> GetSchemaAsync(string? site, CancellationToken cancellationToken) =>
        (await api.SendAsync(new ApiRequest(HttpMethod.Get, "schema") { Site = site }, Json.SchemaDescription, cancellationToken)).Value;

    public async Task<IReadOnlyList<PluginInfo>> GetPluginsAsync(string? site, CancellationToken cancellationToken) =>
        (await api.SendAsync(new ApiRequest(HttpMethod.Get, "plugins") { Site = site }, Json.ItemListPluginInfo, cancellationToken)).Value.Items;

    public async Task<ContentPage> ListEntriesAsync(string type, ContentQuery? query, CancellationToken cancellationToken)
    {
        var queryString = query?.ToQueryString();
        var path = string.IsNullOrEmpty(queryString) ? Content(type) : $"{Content(type)}?{queryString}";
        return (await api.SendAsync(new ApiRequest(HttpMethod.Get, path), Json.ContentPage, cancellationToken)).Value;
    }

    // The answer keeps the read's entity tag, which the save of the form sends back.
    public Task<ApiResponse<ContentEntry>> GetEntryAsync(string type, Guid id, string? locale, CancellationToken cancellationToken) =>
        api.SendAsync(new ApiRequest(HttpMethod.Get, Entry(type, id)) { Locale = locale }, Json.ContentEntry, cancellationToken);

    // The key is the user action's: a retry of the same action passes the same key, so the server replays its answer.
    public Task<ApiResponse<ContentEntry>> CreateEntryAsync(string type, JsonObject fields, string idempotencyKey, string? locale, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        var request = new ApiRequest(HttpMethod.Post, Content(type)) { Body = fields, IdempotencyKey = idempotencyKey, Locale = locale };
        return api.SendAsync(request, Json.ContentEntry, cancellationToken);
    }

    // ifMatch is the entity tag of the read the form was based on, or ApiRequest.NewLocale to add the locale.
    public Task<ApiResponse<ContentEntry>> SaveEntryAsync(string type, Guid id, JsonObject fields, string ifMatch, string? locale, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ifMatch);
        var request = new ApiRequest(HttpMethod.Put, Entry(type, id)) { Body = fields, IfMatch = ifMatch, Locale = locale };
        return api.SendAsync(request, Json.ContentEntry, cancellationToken);
    }

    private static string Content(string type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        return $"content/{Uri.EscapeDataString(type)}";
    }

    private static string Entry(string type, Guid id) => $"{Content(type)}/{id:D}";
}
