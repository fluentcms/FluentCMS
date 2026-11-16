using System.Net;
using System.Text.Json.Nodes;
using FluentCMS.Admin.Content;

namespace FluentCMS.Admin.Api;

// One user action that creates, with what the server keys its Idempotency-Key with, fixed when the action starts: a retry
// passes the same action, so it is the same request even after the shell switched site or locale.
internal sealed record UserAction(string IdempotencyKey, string? Site, string? Locale);

// A form's read of an entry in one locale. A locale the entry does not have yet has no entry, and its tag is
// ApiRequest.NewLocale, which the save sends to add it.
internal sealed record EntryRead(ContentEntry? Entry, string EntityTag)
{
    public bool IsNewLocale => Entry is null;
}

// The typed calls of the core areas (AQ5), over the one pipeline.
internal sealed class ManagementClient(IManagementApi api, ICurrentSite currentSite, IContentLocale contentLocale)
{
    private const string NotFound = "FCMS3104";

    private static ManagementJsonContext Json => ManagementJsonContext.Default;

    // These calls are not about content, so they send no locale, which the server would refuse if the site lacks it.
    public async Task<MeResponse> GetMeAsync(CancellationToken cancellationToken) =>
        (await api.SendAsync(new ApiRequest(HttpMethod.Get, "me") { NoLocale = true }, Json.MeResponse, cancellationToken)).Value;

    public async Task<IReadOnlyList<MySiteInfo>> GetMySitesAsync(CancellationToken cancellationToken) =>
        (await api.SendAsync(new ApiRequest(HttpMethod.Get, "me/sites") { NoLocale = true }, Json.ItemListMySiteInfo, cancellationToken)).Value.Items;

    public async Task<SchemaDescription> GetSchemaAsync(string? site, CancellationToken cancellationToken) =>
        (await api.SendAsync(SiteRead("schema", site), Json.SchemaDescription, cancellationToken)).Value;

    public async Task<IReadOnlyList<PluginInfo>> GetPluginsAsync(string? site, CancellationToken cancellationToken) =>
        (await api.SendAsync(SiteRead("plugins", site), Json.ItemListPluginInfo, cancellationToken)).Value.Items;

    public async Task<ContentPage> ListEntriesAsync(string type, ContentQuery? query, CancellationToken cancellationToken)
    {
        var queryString = query?.ToQueryString();
        var path = string.IsNullOrEmpty(queryString) ? Content(type) : $"{Content(type)}?{queryString}";
        return (await api.SendAsync(new ApiRequest(HttpMethod.Get, path), Json.ContentPage, cancellationToken)).Value;
    }

    // What the locale holds, without the fallback chain, since a read that fell back has no entity tag. A 404 with fallback=none
    // is also what a missing locale answers, so a read through the chain tells it from a missing entry.
    public async Task<EntryRead> GetEntryAsync(string type, Guid id, string? locale, CancellationToken cancellationToken)
    {
        locale ??= contentLocale.Locale;
        try
        {
            var read = await api.SendAsync(new ApiRequest(HttpMethod.Get, $"{Entry(type, id)}?fallback=none") { Locale = locale }, Json.ContentEntry, cancellationToken);
            return new EntryRead(read.Value, read.EntityTag ?? throw new InvalidOperationException($"The read of {type} {id} has no entity tag."));
        }
        catch (ApiProblemException missing) when (missing.Problem is { Status: (int)HttpStatusCode.NotFound, Code: NotFound })
        {
            await api.SendAsync(new ApiRequest(HttpMethod.Get, $"{Entry(type, id)}?fields=id") { Locale = locale }, cancellationToken);
            return new EntryRead(null, ApiRequest.NewLocale);
        }
    }

    // Fixes the key, the site and the locale of a create when the user starts it; a retry of the action passes it again.
    public UserAction StartAction(string? locale = null) =>
        new(ApiRequest.NewIdempotencyKey(), currentSite.Site, locale ?? contentLocale.Locale);

    public Task<ApiResponse<ContentEntry>> CreateEntryAsync(string type, JsonObject fields, UserAction action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        var request = new ApiRequest(HttpMethod.Post, Content(type))
        {
            Body = WithoutSystemMembers(fields),
            IdempotencyKey = action.IdempotencyKey,
            Site = action.Site,
            NoSite = action.Site is null,
            Locale = action.Locale,
            NoLocale = action.Locale is null,
        };
        return api.SendAsync(request, Json.ContentEntry, cancellationToken);
    }

    // ifMatch is EntryRead.EntityTag: the tag of the read the form was based on, or ApiRequest.NewLocale to add the locale.
    public Task<ApiResponse<ContentEntry>> SaveEntryAsync(string type, Guid id, JsonObject fields, string ifMatch, string? locale, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ifMatch);
        var request = new ApiRequest(HttpMethod.Put, Entry(type, id)) { Body = WithoutSystemMembers(fields), IfMatch = ifMatch, Locale = locale };
        return api.SendAsync(request, Json.ContentEntry, cancellationToken);
    }

    private static ApiRequest SiteRead(string path, string? site) =>
        site is null ? new ApiRequest(HttpMethod.Get, path) { NoLocale = true } : new ApiRequest(HttpMethod.Get, path) { Site = site };

    // The server refuses a system member in a write (deletedAt is FCMS1406), so a form's document is sent without them.
    private static JsonObject WithoutSystemMembers(JsonObject fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var body = new JsonObject();
        foreach (var (name, value) in fields.Where(member => !ContentEntry.SystemMembers.Contains(member.Key)))
        {
            body[name] = value?.DeepClone();
        }

        return body;
    }

    private static string Content(string type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        return $"content/{Uri.EscapeDataString(type)}";
    }

    private static string Entry(string type, Guid id) => $"{Content(type)}/{id:D}";
}
