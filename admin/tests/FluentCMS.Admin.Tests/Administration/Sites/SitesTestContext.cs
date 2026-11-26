using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Administration.Sites;

// The sites area against a fake of /sites/{id} and its locales for the shell's site, which keeps what the page changes and
// what GET /me/sites answers in step with it, as the server does. A refusal is given with Management.Answer, which is asked
// first; the locales that entries are in are kept, as the server keeps them (FCMS3136), and so are those other locales name
// in their fallbacks (FCMS3143).
public abstract class SitesTestContext : AdministrationTestContext
{
    protected SitesTestContext()
    {
        Locales.Add(Locale("en", 0));
        Management.Respond = Answer;
    }

    internal JsonArray Locales { get; } = [];

    internal HashSet<string> WithEntries { get; } = new(StringComparer.Ordinal);

    internal JsonObject Site => Management.Sites.Single(site => (Guid)site!["id"]! == MainSite)!.AsObject();

    internal static JsonObject Locale(string code, int position, string[]? fallbacks = null, bool enabled = true) => new()
    {
        ["code"] = code,
        ["position"] = position,
        ["fallbacks"] = fallbacks is null ? null : new JsonArray([.. fallbacks.Select(item => (JsonNode)item)]),
        ["enabled"] = enabled,
    };

    internal void AddLocale(string code, int position, string[]? fallbacks = null, bool enabled = true)
    {
        Locales.Add(Locale(code, position, fallbacks, enabled));
        Sync();
    }

    // The last call of the method to the path, with its body.
    internal string? BodyOf(HttpMethod method, string path) => Management.Calls.Last(call => call.Method == method && call.Path == path).Body;

    internal static IElement Section(IRenderedComponent<AdminApp> app, string heading) =>
        app.FindAll("section.cms-card").Single(section => section.QuerySelector("h2")!.TextContent == heading);

    // A button by its accessible text: the visible text and the hidden part.
    internal static IElement Button(IElement scope, string name) =>
        scope.QuerySelectorAll("fluent-button").Single(button => button.TextContent.Trim() == name);

    internal static bool HasButton(IElement scope, string name) =>
        scope.QuerySelectorAll("fluent-button").Any(button => button.TextContent.Trim() == name);

    internal static IElement Field(IElement scope, string label) =>
        scope.QuerySelector("#" + scope.QuerySelectorAll("label").Single(element => element.TextContent.Trim().TrimEnd('*').TrimEnd() == label).GetAttribute("for"))!;

    internal static string Message(IElement section) => section.QuerySelector(".sites-message")!.TextContent;

    // Each row of the locales: its code, position, state and fallbacks.
    internal static string[][] Rows(IRenderedComponent<AdminApp> app) =>
        [.. app.FindAll("table.sites-locales tbody tr").Select(row => row.Children.Take(4).Select(cell => cell.TextContent.Trim()).ToArray())];

    private JsonObject? Find(string code) => Locales.FirstOrDefault(locale => string.Equals((string)locale!["code"]!, code, StringComparison.OrdinalIgnoreCase))?.AsObject();

    private IEnumerable<JsonObject> Ordered => Locales.Select(locale => locale!.AsObject()).OrderBy(locale => (int)locale["position"]!).ThenBy(locale => (string)locale["code"]!, StringComparer.Ordinal);

    private bool IsDefault(string code) => string.Equals((string)Site["defaultLocale"]!, code, StringComparison.OrdinalIgnoreCase);

    // What GET /me/sites answers of the site's locales.
    private void Sync() =>
        Site["locales"] = new JsonArray([.. Ordered.Select(locale => (JsonNode)new JsonObject { ["code"] = locale["code"]!.DeepClone(), ["position"] = locale["position"]!.DeepClone(), ["enabled"] = locale["enabled"]!.DeepClone() })]);

    private JsonObject SiteRecord() => new()
    {
        ["id"] = MainSite,
        ["name"] = Site["name"]!.DeepClone(),
        ["slug"] = Site["slug"]!.DeepClone(),
        ["defaultLocale"] = Site["defaultLocale"]!.DeepClone(),
        ["isDefault"] = true,
        ["createdAt"] = "2026-11-02T08:00:00+00:00",
    };

    // A fallback is another locale of the site, named once.
    private bool Allowed(JsonNode? fallbacks, string code)
    {
        var chain = fallbacks?.AsArray().Select(item => (string)item!).ToList() ?? [];
        return chain.All(item => Find(item) is not null && !string.Equals(item, code, StringComparison.OrdinalIgnoreCase)) && chain.Distinct(StringComparer.OrdinalIgnoreCase).Count() == chain.Count;
    }

    // The other locales whose fallbacks name the code, in their order; an implicit chain names none.
    private List<string> Naming(string code) =>
        [.. Ordered.Where(locale => !string.Equals((string)locale["code"]!, code, StringComparison.OrdinalIgnoreCase)
                && locale["fallbacks"] is JsonArray chain && chain.Any(item => string.Equals((string)item!, code, StringComparison.OrdinalIgnoreCase)))
            .Select(locale => (string)locale["code"]!)];

    // FCMS3143 as the server writes it: one error for each naming locale at locales.{code}.fallbacks, and a generic detail
    // when there are several.
    private static HttpResponseMessage IsFallback(string code, List<string> naming) => new(HttpStatusCode.Conflict)
    {
        Content = new StringContent(new JsonObject
        {
            ["status"] = 409,
            ["code"] = "FCMS3143",
            ["title"] = "Locale is a fallback",
            ["detail"] = naming.Count == 1 ? $"'{naming[0]}' names '{code}' as a fallback." : $"The request has {naming.Count} errors; see errors.",
            ["errors"] = new JsonArray([.. naming.Select(other => (JsonNode)new JsonObject
            {
                ["path"] = $"locales.{other}.fallbacks",
                ["code"] = "FCMS3143",
                ["message"] = $"'{other}' names '{code}' as a fallback; take '{code}' out of the fallbacks of '{other}' first.",
            })]),
        }.ToJsonString(), Encoding.UTF8, "application/problem+json"),
    };

    private HttpResponseMessage? Answer(HttpRequestMessage request, string path, string? body)
    {
        var segments = path.Split('/').Select(Uri.UnescapeDataString).ToArray();
        if (segments is not ["sites", var id, ..] || Guid.Parse(id) != MainSite)
        {
            return null;
        }

        var method = request.Method;
        var sent = body is null ? null : JsonNode.Parse(body)!.AsObject();
        switch (segments[2..])
        {
            case [] when method == HttpMethod.Get:
                return Json(SiteRecord());
            case [] when method == HttpMethod.Patch:
                if (sent!["name"] is { } name)
                {
                    if (((string)name!).Trim().Length is 0 or > 128)
                    {
                        return Problem(HttpStatusCode.UnprocessableEntity, "FCMS2304", "name");
                    }

                    Site["name"] = ((string)name!).Trim();
                }

                if (sent["defaultLocale"] is { } chosen)
                {
                    if (Find((string)chosen!) is not { } locale || !(bool)locale["enabled"]!)
                    {
                        return Problem(HttpStatusCode.UnprocessableEntity, "FCMS2312", "defaultLocale");
                    }

                    Site["defaultLocale"] = locale["code"]!.DeepClone();
                }

                return Json(SiteRecord());
            case ["locales"] when method == HttpMethod.Get:
                return Json(new JsonObject { ["items"] = new JsonArray([.. Ordered.Select(locale => locale.DeepClone())]) });
            case ["locales"] when method == HttpMethod.Post:
                var code = (string)sent!["code"]!;
                if (Find(code) is not null)
                {
                    return Problem(HttpStatusCode.UnprocessableEntity, "FCMS3137", "code");
                }

                if (!Allowed(sent["fallbacks"], code))
                {
                    return Problem(HttpStatusCode.UnprocessableEntity, "FCMS2312", "fallbacks");
                }

                var added = Locale(code, Locales.Max(locale => (int)locale!["position"]!) + 1, sent["fallbacks"]?.AsArray().Select(item => (string)item!).ToArray(), (bool?)sent["enabled"] ?? true);
                Locales.Add(added);
                Sync();
                return new HttpResponseMessage(HttpStatusCode.Created) { Content = Json(added.DeepClone()).Content };
            case ["locales", var changed] when method == HttpMethod.Patch:
                if (Find(changed) is not { } row)
                {
                    return Problem(HttpStatusCode.UnprocessableEntity, "FCMS3135", "code");
                }

                if (sent!["enabled"] is { } enabled && !(bool)enabled! && IsDefault(changed))
                {
                    return Problem(HttpStatusCode.UnprocessableEntity, "FCMS3136", "enabled");
                }

                if (sent.ContainsKey("fallbacks") && !Allowed(sent["fallbacks"], changed))
                {
                    return Problem(HttpStatusCode.UnprocessableEntity, "FCMS2312", "fallbacks");
                }

                foreach (var (member, value) in sent)
                {
                    row[member] = value?.DeepClone();
                }

                Sync();
                return Json(row.DeepClone());
            case ["locales", var removed] when method == HttpMethod.Delete:
                if (Find(removed) is not { } gone)
                {
                    return Problem(HttpStatusCode.UnprocessableEntity, "FCMS3135", "code");
                }

                if (IsDefault(removed) || WithEntries.Contains(removed))
                {
                    return Problem(HttpStatusCode.UnprocessableEntity, "FCMS3136", "code");
                }

                if (Naming(removed) is { Count: > 0 } naming)
                {
                    return IsFallback(removed, naming);
                }

                Locales.Remove(gone);
                Sync();
                return NoContent();
            default:
                return null;
        }
    }
}
