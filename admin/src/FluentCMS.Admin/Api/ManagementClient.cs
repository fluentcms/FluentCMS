using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Admin.Content;

namespace FluentCMS.Admin.Api;

// One user action that creates, with what the server keys its Idempotency-Key with, fixed when the action starts: a retry
// passes the same action, so it is the same request even after the shell switched site or locale. A null site or locale is
// the default one, and stays so.
internal sealed record UserAction(string IdempotencyKey, string? Site, string? Locale);

// A form's read of an entry in one locale, with the site and locale it was read in, which its save sends again so that a
// view that switched in between cannot write another locale under a matching tag. A locale the entry does not have yet has
// no entry, and its tag is ApiRequest.NewLocale, which the save sends to add it. A create's read has the Location it answered.
internal sealed record EntryRead(ContentEntry? Entry, string EntityTag, string? Site, string? Locale, Uri? Location = null)
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

    // The account page's calls (spec 4.3), the caller's own like GetMeAsync. The two-factor and logins routes answer 403 to a
    // key or a service account.
    public async Task<IReadOnlyList<MySession>> GetMySessionsAsync(CancellationToken cancellationToken) =>
        (await api.SendAsync(Me(HttpMethod.Get, "me/sessions"), Json.ItemListMySession, cancellationToken)).Value.Items;

    public async Task EndMySessionAsync(Guid id, CancellationToken cancellationToken) =>
        await api.SendAsync(Me(HttpMethod.Delete, $"me/sessions/{id:D}"), cancellationToken);

    public async Task<MyTwoFactor> GetTwoFactorAsync(CancellationToken cancellationToken) =>
        (await api.SendAsync(Me(HttpMethod.Get, "me/two-factor"), Json.MyTwoFactor, cancellationToken)).Value;

    public async Task<AuthenticatorSetup> EnrolAuthenticatorAsync(CancellationToken cancellationToken) =>
        (await api.SendAsync(Me(HttpMethod.Post, "me/two-factor/enrol"), Json.AuthenticatorSetup, cancellationToken)).Value;

    public async Task<IReadOnlyList<string>> VerifyAuthenticatorAsync(string code, CancellationToken cancellationToken) =>
        (await api.SendAsync(Me(HttpMethod.Post, "me/two-factor/verify", Code(code)), Json.RecoveryCodeList, cancellationToken)).Value.RecoveryCodes;

    public async Task<IReadOnlyList<string>> NewRecoveryCodesAsync(string code, CancellationToken cancellationToken) =>
        (await api.SendAsync(Me(HttpMethod.Post, "me/two-factor/recovery-codes", Code(code)), Json.RecoveryCodeList, cancellationToken)).Value.RecoveryCodes;

    // An authenticator code or a recovery code.
    public async Task DisableTwoFactorAsync(string code, CancellationToken cancellationToken) =>
        await api.SendAsync(Me(HttpMethod.Post, "me/two-factor/disable", Code(code)), cancellationToken);

    public async Task<IReadOnlyList<MyLogin>> GetMyLoginsAsync(CancellationToken cancellationToken) =>
        (await api.SendAsync(Me(HttpMethod.Get, "me/logins"), Json.ItemListMyLogin, cancellationToken)).Value.Items;

    public async Task RemoveMyLoginAsync(string provider, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        await api.SendAsync(Me(HttpMethod.Delete, $"me/logins/{Uri.EscapeDataString(provider)}"), cancellationToken);
    }

    public async Task<MyPreferences> GetMyPreferencesAsync(CancellationToken cancellationToken) =>
        (await api.SendAsync(Me(HttpMethod.Get, "me/preferences"), Json.MyPreferences, cancellationToken)).Value;

    public async Task<MyPreferences> SetMyPreferencesAsync(MyPreferences preferences, CancellationToken cancellationToken) =>
        (await api.SendAsync(Me(HttpMethod.Put, "me/preferences", JsonSerializer.SerializeToNode(preferences, Json.MyPreferences)), Json.MyPreferences, cancellationToken)).Value;

    // What the caller may do in the site, in the locale named or else the site's default: a grant limited to locales counts
    // only in them, so the server narrows both reads by the locale.
    public Task<MyAccessInfo> GetMyAccessAsync(string? site, CancellationToken cancellationToken) => GetMyAccessAsync(site, null, cancellationToken);

    public async Task<MyAccessInfo> GetMyAccessAsync(string? site, string? locale, CancellationToken cancellationToken) =>
        (await api.SendAsync(Pinned(HttpMethod.Get, "me/access", site, locale), Json.MyAccessInfo, cancellationToken)).Value;

    public Task<SchemaDescription> GetSchemaAsync(string? site, CancellationToken cancellationToken) => GetSchemaAsync(site, null, cancellationToken);

    public async Task<SchemaDescription> GetSchemaAsync(string? site, string? locale, CancellationToken cancellationToken) =>
        (await api.SendAsync(Pinned(HttpMethod.Get, "schema", site, locale), Json.SchemaDescription, cancellationToken)).Value;

    // The entry in every locale it has, by locale, with the status of each (GET /content/{type}/{id}?locale=all), in the site
    // named. An entry the caller may read in no locale is a 404, as a missing one.
    public async Task<IReadOnlyDictionary<string, ContentEntry>> GetEntryLocalesAsync(string type, Guid id, string? site, CancellationToken cancellationToken)
    {
        var read = await api.SendAsync(Pinned(HttpMethod.Get, Entry(type, id), site, "all"), Json.ContentEntry, cancellationToken);
        return EveryLocaleRows.Read([read.Value], null, []).Locales.GetValueOrDefault(id) ?? new Dictionary<string, ContentEntry>();
    }

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
        var answer = await api.SendAsync(request, Json.ContentEntry, cancellationToken);
        return Read(answer, action.Site, action.Locale) with { Location = answer.Location };
    }

    // The id of a singleton's one entry, or null while it has none.
    public async Task<Guid?> FindSingletonAsync(string type, CancellationToken cancellationToken)
    {
        var page = await ListEntriesAsync(type, new ContentQuery { PageSize = 1, Fields = ["id"] }, cancellationToken);
        return page.Items.Count > 0 ? page.Items[0].Id : null;
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

    // In the site and locale of the read, as a save. Unpublish takes no If-Match.
    public async Task<PublishedRead> PublishEntryAsync(string type, Guid id, EntryRead basedOn, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(basedOn);
        var request = Pinned(HttpMethod.Post, $"{Entry(type, id)}/publish", basedOn.Site, basedOn.Locale, ifMatch: basedOn.IsNewLocale ? null : basedOn.EntityTag);
        var answer = await api.SendAsync(request, Json.PublishAnswer, cancellationToken);
        return new PublishedRead(new EntryRead(answer.Value.Entry, answer.EntityTag ?? $"\"{answer.Value.Entry.Version}\"", basedOn.Site, basedOn.Locale), answer.Value.Warnings);
    }

    public async Task<EntryRead> UnpublishEntryAsync(string type, Guid id, EntryRead basedOn, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(basedOn);
        var request = Pinned(HttpMethod.Post, $"{Entry(type, id)}/unpublish", basedOn.Site, basedOn.Locale);
        return Read(await api.SendAsync(request, Json.ContentEntry, cancellationToken), basedOn.Site, basedOn.Locale);
    }

    public async Task<EntryRead> DiscardDraftAsync(string type, Guid id, EntryRead basedOn, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(basedOn);
        var request = Pinned(HttpMethod.Post, $"{Entry(type, id)}/discard", basedOn.Site, basedOn.Locale, ifMatch: basedOn.IsNewLocale ? null : basedOn.EntityTag);
        return Read(await api.SendAsync(request, Json.ContentEntry, cancellationToken), basedOn.Site, basedOn.Locale);
    }

    // The entry as it is now, which a 412 of a save holds, as a read in the site and locale of the save; its version is the
    // tag a save on it sends. Null when the answer holds none that reads.
    public static EntryRead? CurrentOf(ApiProblem problem, EntryRead basedOn)
    {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(basedOn);
        try
        {
            return problem.Current?.Deserialize(Json.ContentEntry) is { } entry
                ? new EntryRead(entry, $"\"{entry.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)}\"", basedOn.Site, basedOn.Locale)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
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

    private static ApiRequest Me(HttpMethod method, string path, JsonNode? body = null) =>
        new(method, path) { Body = body, NoSite = true, NoLocale = true };

    private static JsonObject Code(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return new JsonObject { ["code"] = code };
    }

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
