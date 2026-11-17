using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace FluentCMS.Admin.Tests.Shell;

// The Management API of https://cms.example.com as the shell reads it: /me, /me/sites, and per site /me/access, /schema and
// /plugins; and the caller's own routes of the account page. By default the user has one site, every permission there and no
// content type, no session, no login, two-factor off and no preference. A test changes what it answers.
internal sealed class FakeManagementServer : HttpMessageHandler
{
    public static readonly Guid MainSite = new("0193a1b2-0000-7000-8000-00000000a001");

    private readonly Lock _gate = new();

    public FakeManagementServer() => AddSite(MainSite, "Main", "main", isDefault: true);

    public List<(string Path, string? Site)> Requests { get; } = [];

    public JsonArray Sites { get; } = [];

    // Keyed by the site's id, as the shell sends it in X-Cms-Site.
    public Dictionary<Guid, JsonObject> Access { get; } = [];

    public Dictionary<Guid, JsonObject> Schemas { get; } = [];

    public Dictionary<Guid, JsonArray> Plugins { get; } = [];

    public string UserName { get; set; } = "ada";

    // Every request with its method and body, for the routes a test writes to.
    public List<(HttpMethod Method, string Path, string? Body)> Calls { get; } = [];

    public JsonArray Sessions { get; } = [];

    public JsonArray Logins { get; } = [];

    public JsonObject TwoFactor { get; set; } = new() { ["enabled"] = false, ["required"] = false, ["pending"] = false, ["recoveryCodesLeft"] = 0 };

    public JsonObject Preferences { get; set; } = new() { ["culture"] = null, ["timeZone"] = null, ["dateFormat"] = null, ["numberFormat"] = null };

    // Answers a request before the routes above do, when it returns one.
    public Func<HttpMethod, string, HttpResponseMessage?>? Answer { get; set; }

    // Answers a request with its headers and body, after Answer, when it returns one; the content routes of a form's test.
    public Func<HttpRequestMessage, string, string?, HttpResponseMessage?>? Respond { get; set; }

    // /schema answers 403 even to a holder of schema.read.
    public bool RefusesSchema { get; set; }

    // Every read answers 503 while set.
    public bool Unavailable { get; set; }

    // Every read waits for it while set.
    public TaskCompletionSource? Hold { get; set; }

    // The reads of a site wait for its entry.
    public Dictionary<Guid, TaskCompletionSource> HoldSite { get; } = [];

    public int Count(string path) => Requests.Count(request => request.Path == path);

    public static JsonObject Type(string name, string kind = "collection", string? scope = "site", string? displayName = null, params JsonObject[] fields) => new()
    {
        ["name"] = name,
        ["kind"] = kind,
        ["scope"] = scope,
        ["displayName"] = displayName,
        ["fields"] = new JsonArray([.. fields]),
    };

    public static JsonObject Field(string name, string type = "text", bool required = false, JsonObject? settings = null) => new()
    {
        ["name"] = name,
        ["type"] = type,
        ["validation"] = new JsonObject { ["required"] = required },
        ["settings"] = settings ?? [],
    };

    public static JsonObject Plugin(string name, bool enabled) => new()
    {
        ["name"] = name,
        ["version"] = "1.0.0",
        ["sdkRange"] = "[1.0.0,2.0.0)",
        ["dependsOn"] = new JsonArray(),
        ["enabled"] = enabled,
        ["contributions"] = new JsonObject
        {
            ["fieldTypes"] = new JsonArray(),
            ["permissions"] = new JsonArray(),
            ["endpoints"] = new JsonArray(),
            ["jobs"] = new JsonArray(),
            ["tools"] = new JsonArray(),
        },
        ["sites"] = new JsonArray(),
    };

    public void AddSite(Guid id, string name, string slug, bool isDefault = false)
    {
        Sites.Add(new JsonObject
        {
            ["id"] = id,
            ["name"] = name,
            ["slug"] = slug,
            ["defaultLocale"] = "en",
            ["isDefault"] = isDefault,
            ["locales"] = new JsonArray(new JsonObject { ["code"] = "en", ["position"] = 0, ["enabled"] = true }),
        });
        Grant(id, ["schema.read", "users.manage"]);
        Schemas[id] = new JsonObject { ["types"] = new JsonArray(), ["components"] = new JsonArray(), ["blocks"] = new JsonArray() };
        Plugins[id] = [];
    }

    // The schema of the site holds the types, and the access lets the user read each of them.
    public void SetTypes(Guid site, params JsonObject[] types)
    {
        Schemas[site]["types"] = new JsonArray([.. types]);
        var access = Access[site]["types"]!.AsObject();
        access.Clear();
        foreach (var type in types)
        {
            access[type["name"]!.GetValue<string>()] = new JsonObject
            {
                ["drafts"] = true,
                ["create"] = true,
                ["update"] = true,
                ["delete"] = true,
                ["publish"] = true,
                ["unpublish"] = true,
                ["creatableFields"] = new JsonArray(),
                ["updatableFields"] = new JsonArray(),
                ["conditional"] = new JsonArray(),
            };
        }
    }

    public void Grant(Guid site, string[] permissions) => Access[site] = new JsonObject
    {
        ["site"] = site,
        ["locale"] = "en",
        ["permissions"] = new JsonArray([.. permissions.Select(permission => (JsonNode)permission)]),
        ["instancePermissions"] = new JsonArray(),
        ["limitedPermissions"] = new JsonArray(),
        ["search"] = true,
        ["types"] = Access.TryGetValue(site, out var before) ? before["types"]!.DeepClone() : new JsonObject(),
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath["/api/management/v1/".Length..];
        var siteHeader = request.Headers.TryGetValues("X-Cms-Site", out var values) ? values.Single() : null;
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (_gate)
        {
            Requests.Add((path, siteHeader));
            Calls.Add((request.Method, path, body));
        }

        if (Hold is { } hold)
        {
            await hold.Task.WaitAsync(cancellationToken);
        }

        if (Guid.TryParse(siteHeader, out var held) && HoldSite.TryGetValue(held, out var holdSite))
        {
            await holdSite.Task.WaitAsync(cancellationToken);
        }

        if (Unavailable)
        {
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }

        if (Answer?.Invoke(request.Method, path) is { } answer)
        {
            return answer;
        }

        if (Respond?.Invoke(request, path, body) is { } response)
        {
            return response;
        }

        if (request.Method == HttpMethod.Put && path == "me/preferences")
        {
            Preferences = JsonNode.Parse(body!)!.AsObject();
            return Json(Preferences);
        }

        var site = Guid.TryParse(siteHeader, out var id) ? id : MainSite;
        return path switch
        {
            "me" => Json(new JsonObject { ["id"] = Guid.Empty, ["kind"] = "adminUser", ["name"] = UserName, ["roles"] = new JsonArray() }),
            "me/sites" => Json(new JsonObject { ["items"] = Sites.DeepClone() }),
            "me/access" => Json(Access[site]),
            "schema" when RefusesSchema || !Access[site]["permissions"]!.AsArray().Any(name => name!.GetValue<string>() == "schema.read") => Forbidden(),
            "schema" => Json(Schemas[site]),
            "plugins" => Json(new JsonObject { ["items"] = Plugins[site].DeepClone() }),
            "me/sessions" => Json(new JsonObject { ["items"] = Sessions.DeepClone() }),
            "me/logins" => Json(new JsonObject { ["items"] = Logins.DeepClone() }),
            "me/two-factor" => Json(TwoFactor),
            "me/preferences" => Json(Preferences),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    // A FluentCMS problem; with a path, the code is also that of its one validation error, as the server writes them.
    public static HttpResponseMessage Problem(HttpStatusCode status, string code, string? path = null) =>
        new(status)
        {
            Content = new StringContent(
                new JsonObject
                {
                    ["status"] = (int)status,
                    ["code"] = code,
                    ["title"] = "Problem",
                    ["errors"] = path is null ? new JsonArray() : new JsonArray(new JsonObject { ["path"] = path, ["code"] = code, ["message"] = "Problem" }),
                }.ToJsonString(),
                Encoding.UTF8,
                "application/problem+json"),
        };

    public static HttpResponseMessage NoContent() => new(HttpStatusCode.NoContent);

    public static HttpResponseMessage Json(JsonNode body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Forbidden() =>
        new(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("""{"status":403,"code":"FCMS2001","title":"Forbidden"}""", Encoding.UTF8, "application/problem+json"),
        };
}
