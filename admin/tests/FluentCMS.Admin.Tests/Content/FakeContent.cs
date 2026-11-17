using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FluentCMS.Admin.Tests.Shell;

namespace FluentCMS.Admin.Tests.Content;

internal sealed record ContentWrite(HttpMethod Method, string Path, JsonObject Body, string? IfMatch, string? IdempotencyKey, string? Locale);

// The content routes a form calls, over FakeManagementServer: read an entry with its tag, list a type, create with a
// Location, and save with If-Match, which a stale tag fails with a 412 that holds the entry as it is now. Publish, unpublish
// and discard move the status as the server does. A test can answer the next write itself. A list reads every field of its
// entries in the order they were added, and takes filter[id][in], filter[field][contains], q on every text, pageSize and a
// cursor, which is the number of entries before the page.
internal sealed class FakeContent
{
    private readonly Dictionary<Guid, (string Type, int Version, JsonObject Fields)> _entries = [];
    private readonly Dictionary<Guid, (string Status, JsonObject? Published)> _states = [];

    public FakeContent(FakeManagementServer server) => server.Respond = Handle;

    public List<ContentWrite> Writes { get; } = [];

    public List<string> Reads { get; } = [];

    // The content locale each read was sent in, by its path and query.
    public List<(string Read, string? Locale)> ReadLocales { get; } = [];

    // The locale every entry is read in.
    public string Locale { get; set; } = "en";

    // Answers the next write instead of the store, when it returns an answer, or fails it by throwing.
    public Func<ContentWrite, HttpResponseMessage?>? NextWrite { get; set; }

    // What the next publish warns about, as the server writes its warnings.
    public JsonArray Warnings { get; set; } = [];

    // A published entry starts with its fields as the published version, and a modified one with these.
    public Guid Add(string type, JsonObject fields, int version = 1, Guid? id = null, string status = "draft", JsonObject? published = null)
    {
        var key = id ?? Guid.CreateVersion7();
        _entries[key] = (type, version, fields);
        _states[key] = (status, status switch { "published" => fields.DeepClone().AsObject(), "modified" => published, _ => null });
        return key;
    }

    public string Status(Guid id) => _states[id].Status;

    public JsonObject Fields(Guid id) => _entries[id].Fields;

    public int Version(Guid id) => _entries[id].Version;

    // Another tab's save, so that this form's tag is stale.
    public void ChangeElsewhere(Guid id, string field, string value)
    {
        var (type, version, fields) = _entries[id];
        fields[field] = value;
        _entries[id] = (type, version + 1, fields);
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

        if (request.Method == HttpMethod.Get)
        {
            Reads.Add(request.RequestUri!.PathAndQuery);
            ReadLocales.Add((request.RequestUri.PathAndQuery, Header(request, "X-Cms-Locale")));
            return rest switch
            {
                [] => List(type, System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)),
                [var id] when Guid.TryParse(id, out var key) && _entries.ContainsKey(key) => Entry(key, HttpStatusCode.OK),
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
            var created = Add(type, write.Body);
            var response = Entry(created, HttpStatusCode.Created);
            response.Headers.Location = new Uri($"/api/management/v1/content/{type}/{created:D}", UriKind.Relative);
            return response;
        }

        if (request.Method == HttpMethod.Put && rest is [var saved] && Guid.TryParse(saved, out var savedId))
        {
            var (entryType, version, _) = _entries[savedId];
            if (write.IfMatch != $"\"{version}\"")
            {
                return Problem(HttpStatusCode.PreconditionFailed, new JsonObject
                {
                    ["status"] = 412,
                    ["code"] = "FCMS3105",
                    ["title"] = "The entry was changed",
                    ["detail"] = "Someone saved the entry after you read it.",
                    ["current"] = Document(savedId),
                });
            }

            _entries[savedId] = (entryType, version + 1, write.Body);
            if (_states[savedId] is ("published", var published))
            {
                _states[savedId] = ("modified", published);
            }

            return Entry(savedId, HttpStatusCode.OK);
        }

        if (request.Method == HttpMethod.Post && rest is [var acted, var action] && Guid.TryParse(acted, out var actedId))
        {
            return Act(actedId, action, write);
        }

        return null;
    }

    private HttpResponseMessage List(string type, System.Collections.Specialized.NameValueCollection query)
    {
        IEnumerable<JsonObject> matches = _entries.Where(entry => entry.Value.Type == type).Select(entry => Document(entry.Key));
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

    private HttpResponseMessage Act(Guid id, string action, ContentWrite write)
    {
        var (type, version, fields) = _entries[id];
        if (write.IfMatch is { } tag && tag != $"\"{version}\"")
        {
            return Problem(HttpStatusCode.PreconditionFailed, new JsonObject { ["status"] = 412, ["code"] = "FCMS3105", ["title"] = "The entry was changed", ["current"] = Document(id) });
        }

        var (status, published) = _states[id];
        if (action is "unpublish" or "discard" && published is null)
        {
            return Problem(HttpStatusCode.Conflict, new JsonObject { ["status"] = 409, ["title"] = "This entry is not published in this locale." });
        }

        switch (action)
        {
            case "publish":
                _states[id] = ("published", fields.DeepClone().AsObject());
                var answer = new JsonObject { ["entry"] = Document(id), ["warnings"] = Warnings.DeepClone() };
                Warnings = [];
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer.ToJsonString(), Encoding.UTF8, "application/json") };
                response.Headers.ETag = new($"\"{version}\"");
                return response;
            case "unpublish":
                _states[id] = ("unpublished", null);
                return Entry(id, HttpStatusCode.OK);
            default:
                if (status == "modified")
                {
                    _entries[id] = (type, version + 1, published!.DeepClone().AsObject());
                }

                _states[id] = ("published", published);
                return Entry(id, HttpStatusCode.OK);
        }
    }

    private JsonObject Document(Guid id)
    {
        var (_, version, fields) = _entries[id];
        var document = fields.DeepClone().AsObject();
        document["id"] = id;
        document["status"] = _states[id].Status;
        document["locale"] = Locale;
        document["version"] = version;
        return document;
    }

    private HttpResponseMessage Entry(Guid id, HttpStatusCode status)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(Document(id).ToJsonString(), Encoding.UTF8, "application/json") };
        response.Headers.ETag = new($"\"{_entries[id].Version}\"");
        return response;
    }

    private static string? Header(HttpRequestMessage request, string name) =>
        request.Headers.TryGetValues(name, out var values) ? values.Single() : null;

    private static HttpResponseMessage Problem(HttpStatusCode status, JsonObject body) =>
        new(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/problem+json") };
}
