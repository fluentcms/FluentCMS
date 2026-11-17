using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FluentCMS.Admin.Tests.Shell;

namespace FluentCMS.Admin.Tests.Content;

internal sealed record ContentWrite(HttpMethod Method, string Path, JsonObject Body, string? IfMatch, string? IdempotencyKey, string? Locale);

// The content routes a form calls, over FakeManagementServer: read an entry with its tag, list a singleton, create with a
// Location, and save with If-Match, which a stale tag fails with a 412 that holds the entry as it is now. Publish, unpublish
// and discard move the status as the server does. Each entry is kept per locale: a request is in its locale query parameter,
// else its X-Cms-Locale, else en; a read of a locale the entry lacks is a 404, locale=all answers every locale it has, and a
// save with If-Match "0" adds a locale. A test can answer the next write itself.
internal sealed class FakeContent
{
    public const string DefaultLocale = "en";

    private readonly Dictionary<(Guid Id, string Locale), (string Type, int Version, JsonObject Fields)> _entries = [];
    private readonly Dictionary<(Guid Id, string Locale), (string Status, JsonObject? Published)> _states = [];

    private readonly FakeManagementServer _server;

    public FakeContent(FakeManagementServer server)
    {
        _server = server;
        server.Respond = Handle;
    }

    public List<ContentWrite> Writes { get; } = [];

    public List<string> Reads { get; } = [];

    // Answers the next write instead of the store, when it returns an answer, or fails it by throwing.
    public Func<ContentWrite, HttpResponseMessage?>? NextWrite { get; set; }

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
            return rest switch
            {
                // A list the server holds rows for is the server's to answer.
                [] when _server.Entries.ContainsKey(type) => null,
                [] => FakeManagementServer.Json(new JsonObject
                {
                    ["items"] = new JsonArray([.. _entries.Where(entry => entry.Value.Type == type).Select(entry => entry.Key.Id).Distinct().Select(id => (JsonNode)new JsonObject { ["id"] = id })]),
                    ["pageSize"] = 1,
                }),
                [var id] when Guid.TryParse(id, out var key) && locale == "all" && Locales(key).Count > 0 => FakeManagementServer.Json(new JsonObject
                {
                    ["id"] = key,
                    ["locales"] = new JsonObject(Locales(key).Select(code => KeyValuePair.Create<string, JsonNode?>(code, Document(key, code)))),
                }),
                [var id] when Guid.TryParse(id, out var key) && _entries.ContainsKey((key, locale)) => Entry(key, locale, HttpStatusCode.OK),
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

        if (request.Method == HttpMethod.Post && rest is [var acted, var action] && Guid.TryParse(acted, out var actedId))
        {
            return Act(actedId, locale, action, write);
        }

        return null;
    }

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

    private List<string> Locales(Guid id) => [.. _entries.Keys.Where(key => key.Id == id).Select(key => key.Locale)];

    private JsonObject Document(Guid id, string locale)
    {
        var (_, version, fields) = _entries[(id, locale)];
        var document = fields.DeepClone().AsObject();
        document["id"] = id;
        document["status"] = _states[(id, locale)].Status;
        document["locale"] = locale;
        document["version"] = version;
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
