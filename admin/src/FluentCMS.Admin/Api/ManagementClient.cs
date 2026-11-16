using System.Net;
using System.Text.Json.Nodes;
using FluentCMS.Admin.Content;

namespace FluentCMS.Admin.Api;

// One user action that creates, with what the server keys its Idempotency-Key with, fixed when the action starts: a retry
// passes the same action, so it is the same request even after the shell switched site or locale. A null site or locale is
// the default one, and stays so.
internal sealed record UserAction(string IdempotencyKey, string? Site, string? Locale);

// A form's read of an entry in one locale, with the site and locale it was read in, which its save sends again so that a
// view that switched in between cannot write another locale under a matching tag. A locale the entry does not have yet has
// no entry, and its tag is ApiRequest.NewLocale, which the save sends to add it.
internal sealed record EntryRead(ContentEntry? Entry, string EntityTag, string? Site, string? Locale)
{
    public bool IsNewLocale => Entry is null;
}

// The typed calls of the core areas (AQ5), over the one pipeline.
internal sealed class ManagementClient(IManagementApi api, ICurrentSite currentSite, IContentLocale contentLocale)
{
    private const string NotFound = "FCMS3104";

    private static ManagementJsonContext Json => ManagementJsonContext.Default;

    // The caller's own reads: neither reads a site or a locale, and a stale site or locale would fail them, while me/sites is
    // what the shell reads to recover from one.
    public async Task<MeResponse> GetMeAsync(CancellationToken cancellationToken) =>
        (await api.SendAsync(new ApiRequest(HttpMethod.Get, "me") { NoSite = true, NoLocale = true }, Json.MeResponse, cancellationToken)).Value;

    public async Task<IReadOnlyList<MySiteInfo>> GetMySitesAsync(CancellationToken cancellationToken) =>
        (await api.SendAsync(new ApiRequest(HttpMethod.Get, "me/sites") { NoSite = true, NoLocale = true }, Json.ItemListMySiteInfo, cancellationToken)).Value.Items;

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

    // What the locale holds, without the fallback chain, since a read that fell back has no entity tag. A 404 with
    // fallback=none is also what a missing locale answers, so a read of every locale tells it from a missing entry.
    public async Task<EntryRead> GetEntryAsync(string type, Guid id, string? locale, CancellationToken cancellationToken)
    {
        var site = currentSite.Site;
        locale ??= contentLocale.Locale;
        try
        {
            var read = await api.SendAsync(Pinned(HttpMethod.Get, $"{Entry(type, id)}?fallback=none", site, locale), Json.ContentEntry, cancellationToken);
            return new EntryRead(read.Value, read.EntityTag ?? throw new InvalidOperationException($"The read of {type} {id} has no entity tag."), site, locale);
        }
        catch (ApiProblemException missing) when (missing.Problem is { Status: (int)HttpStatusCode.NotFound, Code: NotFound })
        {
            await api.SendAsync(Pinned(HttpMethod.Get, $"{Entry(type, id)}?fields=id", site, "all"), cancellationToken);
            return new EntryRead(null, ApiRequest.NewLocale, site, locale);
        }
    }

    // Fixes the key, the site and the locale of a create when the user starts it; a retry of the action passes it again.
    public UserAction StartAction(string? locale = null) =>
        new(ApiRequest.NewIdempotencyKey(), currentSite.Site, locale ?? contentLocale.Locale);

    // The answer is the read the form goes on editing, in the action's site and locale.
    public async Task<EntryRead> CreateEntryAsync(string type, JsonObject fields, UserAction action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(action.IdempotencyKey);
        var request = Pinned(HttpMethod.Post, Content(type), action.Site, action.Locale, WithoutSystemMembers(fields), idempotencyKey: action.IdempotencyKey);
        return Read(await api.SendAsync(request, Json.ContentEntry, cancellationToken), action.Site, action.Locale);
    }

    // Saves in exactly the site and locale of the read, with its tag: the version it was based on, or "0" to add the locale.
    // The answer is the read the next save is based on.
    public async Task<EntryRead> SaveEntryAsync(string type, Guid id, JsonObject fields, EntryRead basedOn, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(basedOn);
        ArgumentException.ThrowIfNullOrWhiteSpace(basedOn.EntityTag);
        var request = Pinned(HttpMethod.Put, Entry(type, id), basedOn.Site, basedOn.Locale, WithoutSystemMembers(fields), ifMatch: basedOn.EntityTag);
        return Read(await api.SendAsync(request, Json.ContentEntry, cancellationToken), basedOn.Site, basedOn.Locale);
    }

    private static EntryRead Read(ApiResponse<ContentEntry> answer, string? site, string? locale) =>
        new(answer.Value, answer.EntityTag ?? $"\"{answer.Value.Version}\"", site, locale);

    // A null site or locale is sent as none, so that the shell's or the view's cannot slip in after the request was made.
    private static ApiRequest Pinned(HttpMethod method, string path, string? site, string? locale, JsonNode? body = null, string? ifMatch = null, string? idempotencyKey = null) =>
        new(method, path)
        {
            Body = body,
            IfMatch = ifMatch,
            IdempotencyKey = idempotencyKey,
            Site = site,
            NoSite = site is null,
            Locale = locale,
            NoLocale = locale is null,
        };

    private static ApiRequest SiteRead(string path, string? site) => Pinned(HttpMethod.Get, path, site, locale: null);

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
