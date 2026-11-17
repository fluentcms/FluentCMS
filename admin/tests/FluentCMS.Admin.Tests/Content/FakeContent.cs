using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FluentCMS.Admin.Tests.Shell;

namespace FluentCMS.Admin.Tests.Content;

internal sealed record ContentWrite(HttpMethod Method, string Path, JsonObject Body, string? IfMatch, string? IdempotencyKey, string? Locale);

// The content routes a form calls, over FakeManagementServer: read an entry with its tag, list a singleton, create with a
// Location, and save with If-Match, which a stale tag fails with a 412 that holds the entry as it is now. A test can answer
// the next write itself.
internal sealed class FakeContent
{
    private readonly Dictionary<Guid, (string Type, int Version, JsonObject Fields)> _entries = [];

    public FakeContent(FakeManagementServer server) => server.Respond = Handle;

    public List<ContentWrite> Writes { get; } = [];

    public List<string> Reads { get; } = [];

    // Answers the next write instead of the store, when it returns an answer.
    public Func<ContentWrite, HttpResponseMessage?>? NextWrite { get; set; }

    public Guid Add(string type, JsonObject fields, int version = 1, Guid? id = null)
    {
        var key = id ?? Guid.CreateVersion7();
        _entries[key] = (type, version, fields);
        return key;
    }

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
            return rest switch
            {
                [] => FakeManagementServer.Json(new JsonObject
                {
                    ["items"] = new JsonArray([.. _entries.Where(entry => entry.Value.Type == type).Select(entry => (JsonNode)new JsonObject { ["id"] = entry.Key })]),
                    ["pageSize"] = 1,
                }),
                [var id] when Guid.TryParse(id, out var key) && _entries.ContainsKey(key) => Entry(key, HttpStatusCode.OK),
                _ => FakeManagementServer.Problem(HttpStatusCode.NotFound, "FCMS3104"),
            };
        }

        var write = new ContentWrite(
            request.Method,
            path,
            JsonNode.Parse(body!)!.AsObject(),
            Header(request, "If-Match"),
            Header(request, "Idempotency-Key"),
            Header(request, "X-Cms-Locale"));
        Writes.Add(write);
        if (NextWrite?.Invoke(write) is { } answer)
        {
            NextWrite = null;
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
            return Entry(savedId, HttpStatusCode.OK);
        }

        return null;
    }

    private JsonObject Document(Guid id)
    {
        var (_, version, fields) = _entries[id];
        var document = fields.DeepClone().AsObject();
        document["id"] = id;
        document["status"] = "draft";
        document["locale"] = "en";
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
