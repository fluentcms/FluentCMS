using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FluentCMS.Admin.Tests.Shell;

namespace FluentCMS.Admin.Tests.Content;

internal sealed record ContentWrite(HttpMethod Method, string Path, JsonObject Body, string? IfMatch, string? IdempotencyKey, string? Locale);

// The content routes a form calls, over FakeManagementServer: read an entry with its tag, list a type, create with a
// Location, and save with If-Match, which a stale tag fails with a 412 that holds the entry as it is now. Publish, unpublish
// and discard move the status as the server does. Each entry is kept per locale: a request is in its locale query parameter,
// else its X-Cms-Locale, else en; a read of a locale the entry lacks is a 404, locale=all answers every locale it has, and a
// save with If-Match "0" adds a locale. A test can answer the next write itself. A list reads every field of its entries in
// its locale, in the order they were added, and takes filter[id][in], filter[field][contains], q on every text, pageSize
// and a cursor, which is the number of entries before the page. Versions are what a test adds, listed newest first and read
// one by one, each in its locale; a diff answers the changes a test set for its locale and pair, and a restore with a current
// tag makes the version's fields the draft as a new version. A preview token's answer has the URLs a test gives for the entry
// and locale, each draft URL with a token of its own, and none by default, as for a type without a template.
internal sealed class FakeContent
{
    public const string DefaultLocale = "en";

    private readonly Dictionary<(Guid Id, string Locale), (string Type, int Version, JsonObject Fields)> _entries = [];
    private readonly Dictionary<(Guid Id, string Locale), (string Status, JsonObject? Published)> _states = [];

    // The pending schedules, by entry, locale and action, as instants.
    private readonly Dictionary<(Guid Id, string Locale, string Action), DateTimeOffset> _schedules = [];

    private readonly FakeManagementServer _server;

    public FakeContent(FakeManagementServer server)
    {
        _server = server;
        server.Respond = Handle;
    }

    public List<ContentWrite> Writes { get; } = [];

    public List<string> Reads { get; } = [];

    // The content locale each read was sent in, by its path and query.
    public List<(string Read, string? Locale)> ReadLocales { get; } = [];

    // Answers the next write instead of the store, when it returns an answer, or fails it by throwing.
    public Func<ContentWrite, HttpResponseMessage?>? NextWrite { get; set; }

    public Dictionary<(Guid Id, string Locale), List<(JsonObject Item, JsonObject Fields)>> Versions { get; } = [];

    public Dictionary<(string Locale, int From, int To), JsonArray> Diffs { get; } = [];

    public void AddVersion(Guid id, int number, JsonObject fields, string authorName = "Ada", string authorKind = "adminUser", string origin = "admin", string locale = DefaultLocale)
    {
        var item = new JsonObject
        {
            ["number"] = number,
            ["locale"] = locale,
            ["status"] = "draft",
            ["authorId"] = Guid.Empty,
            ["authorKind"] = authorKind,
            ["authorName"] = authorName,
            ["origin"] = origin,
            ["createdAt"] = new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero).AddMinutes(number),
        };
        (Versions.TryGetValue((id, locale), out var list) ? list : Versions[(id, locale)] = []).Add((item, fields));
    }

    // The draft and published URLs of a preview, by entry and the token's locale; {token} is filled in.
    public Func<Guid, string?, (string? Url, string? PublishedUrl)>? Preview { get; set; }

    public TimeSpan PreviewLifetime { get; set; } = TimeSpan.FromMinutes(10);

    // The server's clock for a preview token, sent as the answer's Date while set; the browser's clock while not, with no Date.
    public DateTimeOffset? PreviewClock { get; set; }

    // The token's lifetime in seconds the answer gives as expiresIn (#3473), while set.
    public int? PreviewExpiresIn { get; set; }

    public int PreviewMints { get; private set; }

    // A mint is refused while set.
    public bool PreviewUnavailable { get; set; }

    // What the next publish warns about, as the server writes its warnings.
    public JsonArray Warnings { get; set; } = [];

    // A published entry starts with its fields as the published version, and a modified one with these.
    public Guid Add(string type, JsonObject fields, int version = 1, Guid? id = null, string status = "draft", JsonObject? published = null, string locale = DefaultLocale)
    {
        var key = id ?? Guid.CreateVersion7();
        _entries[(key, locale)] = (type, version, fields);
        _states[(key, locale)] = (status, status switch { "published" => fields.DeepClone().AsObject(), "modified" => published, _ => null });
        return key;
    }

    // The clock a schedule must be after.
    public DateTimeOffset Now { get; set; } = new(2026, 11, 19, 12, 20, 0, TimeSpan.Zero);

    public DateTimeOffset? Scheduled(Guid id, string action, string locale = DefaultLocale) =>
        _schedules.TryGetValue((id, locale, action), out var at) ? at : null;

    public void Schedule(Guid id, string action, DateTimeOffset at, string locale = DefaultLocale) => _schedules[(id, locale, action)] = at;

    public bool Has(Guid id, string locale) => _entries.ContainsKey((id, locale));

    public string Status(Guid id, string locale = DefaultLocale) => _states[(id, locale)].Status;

    public JsonObject Fields(Guid id, string locale = DefaultLocale) => _entries[(id, locale)].Fields;

    public int Version(Guid id, string locale = DefaultLocale) => _entries[(id, locale)].Version;

    // Another tab's save, so that this form's tag is stale.
    public void ChangeElsewhere(Guid id, string field, string value, string locale = DefaultLocale)
    {
        var (type, version, fields) = _entries[(id, locale)];
        fields[field] = value;
        _entries[(id, locale)] = (type, version + 1, fields);
    }

    public static HttpResponseMessage Invalid(params (string Path, string Message)[] errors) => Problem(HttpStatusCode.UnprocessableEntity, new JsonObject
    {
        ["status"] = 422,
        ["code"] = "FCMS2103",
        ["title"] = "The entry is not valid",
        ["errors"] = new JsonArray([.. errors.Select(error => (JsonNode)new JsonObject { ["path"] = error.Path, ["code"] = "FCMS2103", ["message"] = error.Message })]),
    });

    private HttpResponseMessage? Handle(HttpRequestMessage request, string path, string? body)
    {
        if (path.Split('/') is not ["content", var type, .. var rest])
        {
            return null;
        }

        var locale = LocaleOf(request);
        if (request.Method == HttpMethod.Get)
        {
            Reads.Add(request.RequestUri!.PathAndQuery);
            ReadLocales.Add((request.RequestUri.PathAndQuery, Header(request, "X-Cms-Locale")));
            return rest switch
            {
                // A list the server holds rows for is the server's to answer.
                [] when _server.Entries.ContainsKey(type) => null,
                [] => List(type, locale, System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)),
                [var id] when Guid.TryParse(id, out var key) && locale == "all" && Locales(key).Count > 0 => FakeManagementServer.Json(new JsonObject
                {
                    ["id"] = key,
                    ["locales"] = new JsonObject(Locales(key).Select(code => KeyValuePair.Create<string, JsonNode?>(code, Document(key, code)))),
                }),
                [var id] when Guid.TryParse(id, out var key) && _entries.ContainsKey((key, locale)) => Entry(key, locale, HttpStatusCode.OK),
                [var id, "versions"] when Guid.TryParse(id, out var key) && _entries.ContainsKey((key, locale)) => FakeManagementServer.Json(new JsonObject
                {
                    ["items"] = new JsonArray([.. VersionsOf(key, locale).AsEnumerable().Reverse().Select(version => (JsonNode)version.Item.DeepClone())]),
                }),
                [var id, "versions", var number] when Guid.TryParse(id, out var key) && VersionsOf(key, locale).FirstOrDefault(version => version.Item["number"]!.ToString() == number) is { Fields: not null } version =>
                    FakeManagementServer.Json(VersionDocument(key, locale, version.Item, version.Fields)),
                [_, "versions", var left, "diff", var right] => FakeManagementServer.Json(new JsonObject
                {
                    ["from"] = int.Parse(left, System.Globalization.CultureInfo.InvariantCulture),
                    ["to"] = int.Parse(right, System.Globalization.CultureInfo.InvariantCulture),
                    ["changes"] = Diffs.GetValueOrDefault((locale, int.Parse(left, System.Globalization.CultureInfo.InvariantCulture), int.Parse(right, System.Globalization.CultureInfo.InvariantCulture)))?.DeepClone() ?? new JsonArray(),
                }),
                _ => FakeManagementServer.Problem(HttpStatusCode.NotFound, "FCMS3104"),
            };
        }

        var write = new ContentWrite(
            request.Method,
            path,
            string.IsNullOrEmpty(body) ? [] : JsonNode.Parse(body)!.AsObject(),
            Header(request, "If-Match"),
            Header(request, "Idempotency-Key"),
            Header(request, "X-Cms-Locale"));
        Writes.Add(write);
        var next = NextWrite;
        NextWrite = null;
        if (next?.Invoke(write) is { } answer)
        {
            return answer;
        }

        if (request.Method == HttpMethod.Post && rest is [var previewed, "preview-token"] && Guid.TryParse(previewed, out var previewedId))
        {
            var minted = ++PreviewMints;
            if (PreviewUnavailable)
            {
                return FakeManagementServer.Problem(HttpStatusCode.Forbidden, "FCMS1000");
            }

            var (url, publishedUrl) = Preview?.Invoke(previewedId, write.Body["locale"]?.GetValue<string>()) ?? (null, null);
            var minting = new JsonObject
            {
                ["token"] = $"token-{minted}",
                ["expiresAt"] = (PreviewClock ?? DateTimeOffset.UtcNow) + PreviewLifetime,
                ["url"] = url?.Replace("{token}", $"token-{minted}", StringComparison.Ordinal),
                ["publishedUrl"] = publishedUrl,
            };
            if (PreviewExpiresIn is { } seconds)
            {
                minting["expiresIn"] = seconds;
            }

            var token = FakeManagementServer.Json(minting);
            token.Headers.Date = PreviewClock;
            return token;
        }

        if (request.Method == HttpMethod.Post && rest is [])
        {
            var created = Add(type, write.Body, locale: locale);
            var response = Entry(created, locale, HttpStatusCode.Created);
            response.Headers.Location = new Uri($"/api/management/v1/content/{type}/{created:D}", UriKind.Relative);
            return response;
        }

        if (request.Method == HttpMethod.Put && rest is [var saved] && Guid.TryParse(saved, out var savedId))
        {
            if (!_entries.TryGetValue((savedId, locale), out var stored))
            {
                if (Locales(savedId).Count == 0)
                {
                    return FakeManagementServer.Problem(HttpStatusCode.NotFound, "FCMS3104");
                }

                // A locale the entry does not have yet is version 0, which "0" adds.
                stored = (_entries.First(entry => entry.Key.Id == savedId).Value.Type, 0, []);
                if (write.IfMatch == "\"0\"")
                {
                    _states[(savedId, locale)] = ("draft", null);
                }
            }

            var (entryType, version, _) = stored;
            if (write.IfMatch != $"\"{version}\"")
            {
                return Problem(HttpStatusCode.PreconditionFailed, new JsonObject
                {
                    ["status"] = 412,
                    ["code"] = "FCMS3105",
                    ["title"] = "The entry was changed",
                    ["detail"] = "Someone saved the entry after you read it.",
                    ["current"] = version == 0 ? null : Document(savedId, locale),
                });
            }

            _entries[(savedId, locale)] = (entryType, version + 1, write.Body);
            if (_states[(savedId, locale)] is ("published", var published))
            {
                _states[(savedId, locale)] = ("modified", published);
            }

            return Entry(savedId, locale, HttpStatusCode.OK);
        }

        if (rest is [var scheduled, "schedule", ..] && Guid.TryParse(scheduled, out var scheduledId))
        {
            return ScheduleWrite(scheduledId, locale, rest[2..], write);
        }

        if (request.Method == HttpMethod.Post && rest is [var restoredId, "versions", var restoredNumber, "restore"] && Guid.TryParse(restoredId, out var restoredKey))
        {
            return Restore(restoredKey, locale, restoredNumber, write);
        }

        if (request.Method == HttpMethod.Post && rest is [var acted, var action] && Guid.TryParse(acted, out var actedId))
        {
            return Act(actedId, locale, action, write);
        }

        return null;
    }

    private HttpResponseMessage List(string type, string locale, System.Collections.Specialized.NameValueCollection query)
    {
        IEnumerable<JsonObject> matches = _entries.Where(entry => entry.Value.Type == type && entry.Key.Locale == locale).Select(entry => Document(entry.Key.Id, locale));
        foreach (var key in query.AllKeys.OfType<string>())
        {
            var value = query[key]!;
            if (key == "filter[id][in]")
            {
                var ids = value.Split(',').Select(Guid.Parse).ToHashSet();
                matches = matches.Where(entry => ids.Contains(entry["id"]!.GetValue<Guid>()));
            }
            else if (key.StartsWith("filter[", StringComparison.Ordinal) && key.EndsWith("][contains]", StringComparison.Ordinal))
            {
                var field = key["filter[".Length..^"][contains]".Length];
                matches = matches.Where(entry => Text(entry[field])?.Contains(value, StringComparison.OrdinalIgnoreCase) == true);
            }
            else if (key == "q")
            {
                matches = matches.Where(entry => entry.Any(member => !FluentCMS.Admin.Api.ContentEntry.SystemMembers.Contains(member.Key) && Text(member.Value)?.Contains(value, StringComparison.OrdinalIgnoreCase) == true));
            }
        }

        var all = matches.ToList();
        var skip = int.TryParse(query["cursor"], out var cursor) ? cursor : 0;
        var size = int.TryParse(query["pageSize"], out var pageSize) ? pageSize : 25;
        var page = new JsonObject { ["items"] = new JsonArray([.. all.Skip(skip).Take(size)]), ["pageSize"] = size };
        if (skip + size < all.Count)
        {
            page["nextCursor"] = (skip + size).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return FakeManagementServer.Json(page);
    }

    private static string? Text(JsonNode? node) => node is JsonValue value && value.GetValueKind() == System.Text.Json.JsonValueKind.String ? value.GetValue<string>() : null;

    private HttpResponseMessage Act(Guid id, string locale, string action, ContentWrite write)
    {
        if (!_entries.TryGetValue((id, locale), out var stored))
        {
            return FakeManagementServer.Problem(HttpStatusCode.NotFound, "FCMS3104");
        }

        var (type, version, fields) = stored;
        if (write.IfMatch is { } tag && tag != $"\"{version}\"")
        {
            return Problem(HttpStatusCode.PreconditionFailed, new JsonObject { ["status"] = 412, ["code"] = "FCMS3105", ["title"] = "The entry was changed", ["current"] = Document(id, locale) });
        }

        var (status, published) = _states[(id, locale)];
        if (action is "unpublish" or "discard" && published is null)
        {
            return Problem(HttpStatusCode.Conflict, new JsonObject { ["status"] = 409, ["title"] = "This entry is not published in this locale." });
        }

        switch (action)
        {
            case "publish":
                _states[(id, locale)] = ("published", fields.DeepClone().AsObject());
                var answer = new JsonObject { ["entry"] = Document(id, locale), ["warnings"] = Warnings.DeepClone() };
                Warnings = [];
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer.ToJsonString(), Encoding.UTF8, "application/json") };
                response.Headers.ETag = new($"\"{version}\"");
                return response;
            case "unpublish":
                _states[(id, locale)] = ("unpublished", null);
                return Entry(id, locale, HttpStatusCode.OK);
            default:
                if (status == "modified")
                {
                    _entries[(id, locale)] = (type, version + 1, published!.DeepClone().AsObject());
                }

                _states[(id, locale)] = ("published", published);
                return Entry(id, locale, HttpStatusCode.OK);
        }
    }

    // As the server: a wall-clock time in an IANA zone, refused in the past or in an hour the clocks skip; a second schedule
    // of an action replaces the first, and a cancel of none is a 422.
    private HttpResponseMessage ScheduleWrite(Guid id, string locale, string[] action, ContentWrite write)
    {
        if (!_entries.ContainsKey((id, locale)))
        {
            return FakeManagementServer.Problem(HttpStatusCode.NotFound, "FCMS3104");
        }

        if (write.Method == HttpMethod.Delete && action is [var cancelled])
        {
            return _schedules.Remove((id, locale, cancelled))
                ? new HttpResponseMessage(HttpStatusCode.NoContent)
                : Invalid(("", "Nothing is scheduled."));
        }

        var name = write.Body["action"]!.GetValue<string>();
        var zoneId = write.Body["timeZone"]!.GetValue<string>();
        var local = DateTime.Parse(write.Body["at"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        if (zone.IsInvalidTime(local))
        {
            return Invalid(("at", $"That time does not exist in {zoneId}: the clocks skip it."));
        }

        var runAt = new DateTimeOffset(local, zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local));
        if (runAt <= Now)
        {
            return Invalid(("at", "The time is not in the future."));
        }

        _schedules[(id, locale, name)] = runAt;
        return FakeManagementServer.Json(new JsonObject { ["action"] = name, ["locale"] = locale, ["runAt"] = runAt.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture), ["timeZone"] = zoneId, ["version"] = _entries[(id, locale)].Version });
    }

    private List<(JsonObject Item, JsonObject Fields)> VersionsOf(Guid id, string locale) => Versions.GetValueOrDefault((id, locale)) ?? [];

    private static JsonObject VersionDocument(Guid id, string locale, JsonObject item, JsonObject fields)
    {
        var document = fields.DeepClone().AsObject();
        document["id"] = id;
        document["status"] = item["status"]!.DeepClone();
        document["locale"] = locale;
        document["version"] = item["number"]!.DeepClone();
        return document;
    }

    private HttpResponseMessage Restore(Guid id, string locale, string number, ContentWrite write)
    {
        if (!_entries.TryGetValue((id, locale), out var stored)
            || VersionsOf(id, locale).FirstOrDefault(version => version.Item["number"]!.ToString() == number) is not { Fields: not null } version)
        {
            return FakeManagementServer.Problem(HttpStatusCode.NotFound, "FCMS3104");
        }

        var (type, current, _) = stored;
        if (write.IfMatch is { } tag && tag != $"\"{current}\"")
        {
            return Problem(HttpStatusCode.PreconditionFailed, new JsonObject { ["status"] = 412, ["code"] = "FCMS3105", ["title"] = "The entry was changed", ["current"] = Document(id, locale) });
        }

        _entries[(id, locale)] = (type, current + 1, version.Fields.DeepClone().AsObject());
        AddVersion(id, current + 1, version.Fields.DeepClone().AsObject(), locale: locale);
        return Entry(id, locale, HttpStatusCode.OK);
    }

    private List<string> Locales(Guid id) => [.. _entries.Keys.Where(key => key.Id == id).Select(key => key.Locale)];

    private JsonObject Document(Guid id, string locale)
    {
        var (_, version, fields) = _entries[(id, locale)];
        var document = fields.DeepClone().AsObject();
        document["id"] = id;
        document["status"] = _states[(id, locale)].Status;
        document["locale"] = locale;
        document["version"] = version;
        foreach (var (action, member) in new[] { ("publish", "scheduledPublishAt"), ("unpublish", "scheduledUnpublishAt") })
        {
            if (_schedules.TryGetValue((id, locale, action), out var at))
            {
                document[member] = at.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        return document;
    }

    private HttpResponseMessage Entry(Guid id, string locale, HttpStatusCode status)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(Document(id, locale).ToJsonString(), Encoding.UTF8, "application/json") };
        response.Headers.ETag = new($"\"{_entries[(id, locale)].Version}\"");
        return response;
    }

    private static string LocaleOf(HttpRequestMessage request)
    {
        var query = request.RequestUri!.Query.TrimStart('?').Split('&').FirstOrDefault(pair => pair.StartsWith("locale=", StringComparison.Ordinal));
        return query is not null ? Uri.UnescapeDataString(query["locale=".Length..]) : Header(request, "X-Cms-Locale") ?? DefaultLocale;
    }

    private static string? Header(HttpRequestMessage request, string name) =>
        request.Headers.TryGetValues(name, out var values) ? values.Single() : null;

    private static HttpResponseMessage Problem(HttpStatusCode status, JsonObject body) =>
        new(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/problem+json") };
}
