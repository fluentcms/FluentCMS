using System.Net;
using System.Text.Json.Nodes;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Administration.Settings;

// The settings routes of the Management API as the server answers them: GET /settings lists Areas, and each area is read,
// written and reset at /settings/{area} and /settings/{area}/defaults over its version. A write keeps a secret it leaves
// out and makes one it names the scope's own, as the server does.
internal sealed class FakeSettings
{
    public JsonArray Areas { get; } = [];

    public Dictionary<string, Document> Documents { get; } = new(StringComparer.Ordinal);

    // Every write and reset, with its If-Match and body.
    public List<(HttpMethod Method, string Path, string? IfMatch, JsonObject? Body)> Writes { get; } = [];

    // A problem the next write answers instead, once.
    public HttpResponseMessage? NextWrite { get; set; }

    // Runs before a write is answered, as another writer would, such as a change made elsewhere.
    public Action? BeforeWrite { get; set; }

    public static JsonObject Area(string area, string? plugin = null, bool site = true, bool defaults = true) =>
        new() { ["area"] = area, ["plugin"] = plugin, ["site"] = site, ["defaults"] = defaults };

    public Document Add(string path, JsonObject schema, JsonObject value, string source = "default", int version = 0)
    {
        var document = new Document { Schema = schema, Value = value, Source = source, Version = version };
        Documents[path] = document;
        return document;
    }

    public HttpResponseMessage? Respond(HttpRequestMessage request, string path, string? body)
    {
        if (path == "settings" && request.Method == HttpMethod.Get)
        {
            return Json(new JsonObject { ["items"] = Areas.DeepClone() });
        }

        if (!path.StartsWith("settings/", StringComparison.Ordinal) || !Documents.TryGetValue(path["settings/".Length..], out var document))
        {
            return null;
        }

        if (request.Method == HttpMethod.Get)
        {
            return Json(document.Read(path));
        }

        var ifMatch = request.Headers.TryGetValues("If-Match", out var values) ? values.Single() : null;
        var sent = body is null ? null : JsonNode.Parse(body)!.AsObject();
        Writes.Add((request.Method, path, ifMatch, sent));
        BeforeWrite?.Invoke();
        BeforeWrite = null;
        if (NextWrite is { } answer)
        {
            NextWrite = null;
            return answer;
        }

        if (ifMatch != $"\"{document.Version}\"")
        {
            return Problem(HttpStatusCode.PreconditionFailed, "FCMS2808");
        }

        var defaults = path.EndsWith("/defaults", StringComparison.Ordinal);
        if (request.Method == HttpMethod.Delete)
        {
            document.Value = document.ResetTo.DeepClone().AsObject();
            (document.Version, document.Source) = (0, defaults ? "code" : "default");
            document.SecretsInherited = defaults ? [] : [.. document.Secrets];
            document.SecretsSet = [.. document.ResetSecretsSet];
            return NoContent();
        }

        foreach (var (name, value) in sent!)
        {
            if (document.Secrets.Contains(name))
            {
                document.SecretsInherited.Remove(name);
                document.SecretsSet.Remove(name);
                if (value is not null)
                {
                    document.SecretsSet.Add(name);
                }
            }
            else
            {
                document.Value[name] = value?.DeepClone();
            }
        }

        (document.Version, document.Source) = (document.Version + 1, defaults ? "default" : "site");
        return Json(document.Read(path));
    }

    internal sealed class Document
    {
        public required JsonObject Schema { get; init; }

        public required JsonObject Value { get; set; }

        public required string Source { get; set; }

        public int Version { get; set; }

        public List<string> SecretsSet { get; set; } = [];

        public List<string> SecretsInherited { get; set; } = [];

        // What a reset leaves: the document the scope then follows, and the secrets set there.
        public JsonObject ResetTo { get; set; } = [];

        public List<string> ResetSecretsSet { get; set; } = [];

        public IEnumerable<string> Secrets => Schema["properties"]!.AsObject()
            .Where(property => property.Value?["writeOnly"]?.GetValue<bool>() == true).Select(property => property.Key);

        public JsonObject Read(string path) => new()
        {
            ["area"] = path["settings/".Length..].Split('/')[0],
            ["siteId"] = path.EndsWith("/defaults", StringComparison.Ordinal) ? null : MainSite,
            ["source"] = Source,
            ["version"] = Version,
            ["value"] = Value.DeepClone(),
            ["secretsSet"] = new JsonArray([.. SecretsSet.Select(name => (JsonNode)name)]),
            ["secretsInherited"] = new JsonArray([.. SecretsInherited.Select(name => (JsonNode)name)]),
            ["schema"] = Schema.DeepClone(),
        };
    }
}
