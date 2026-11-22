using System.Net;
using System.Text.Json.Nodes;
using Bunit;
using FluentCMS.Admin.Components;
using FluentCMS.Admin.Plugins;
using Microsoft.Extensions.DependencyInjection;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Administration.Plugins;

// The plugins area against a fake of GET /plugins and PUT /plugins/{name}/sites/{site} that keeps the enablement of each
// plugin in each site, as the server does: the list says whether a plugin is enabled in the site of the request, and lists
// its enablement in the sites where the caller manages plugins. The app has the sample half of the tests, the half of the
// plugin reviews. A refusal is given with Management.Answer, which is asked first.
public abstract class PluginsTestContext : AdministrationTestContext
{
    internal static readonly Guid Shop = new("0193a1b2-0000-7000-8000-00000000a002");

    protected PluginsTestContext()
    {
        var sample = AdminPluginManifestJson.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, AdminPluginManifest.FileName)));
        Services.AddSingleton(new AdminPluginCatalog([sample], []));
        Management.AddSite(Shop, "Shop", "shop");
        Installed.Add(Plugin("reviews", "1.2.0", fieldTypes: ["rating"], permissions: ["reviews.notes.manage"], jobs: ["reviews.digest"]));
        Installed.Add(Plugin("search", "1.0.0", permissions: ["search.manage"], endpoints: [("POST", "/api/management/v1/search/rebuild")], jobs: ["search.rebuild"]));
        Managed.AddRange([MainSite, Shop]);
        Management.Respond = Answer;
    }

    // The plugins the host runs, without their enablement.
    internal List<JsonObject> Installed { get; } = [];

    // The plugins disabled in a site.
    internal HashSet<(string Plugin, Guid Site)> Disabled { get; } = [];

    // The sites where the caller manages plugins, the default first.
    internal List<Guid> Managed { get; } = [];

    internal static JsonObject Plugin(string name, string version, string[]? fieldTypes = null, string[]? permissions = null, (string Method, string Route)[]? endpoints = null, string[]? jobs = null) => new()
    {
        ["name"] = name,
        ["version"] = version,
        ["sdkRange"] = "[1.0.0,2.0.0)",
        ["dependsOn"] = new JsonArray(),
        ["contributions"] = new JsonObject
        {
            ["fieldTypes"] = Array(fieldTypes),
            ["permissions"] = Array(permissions),
            ["endpoints"] = new JsonArray([.. (endpoints ?? []).Select(endpoint => (JsonNode)new JsonObject { ["method"] = endpoint.Method, ["route"] = endpoint.Route })]),
            ["jobs"] = Array(jobs),
            ["tools"] = new JsonArray(),
        },
    };

    internal int PluginReads => Management.Count("plugins");

    // A switch by its label, of the plugin at that place of the list: the first is reviews.
    internal static IRenderedComponent<CmsSwitch> Switch(IRenderedComponent<AdminApp> app, string label, int plugin = 0) =>
        app.FindComponents<CmsSwitch>().Where(toggle => toggle.Instance.Label == label).ElementAt(plugin);

    // The card of a plugin, a section headed by its name.
    internal static AngleSharp.Dom.IElement Card(IRenderedComponent<AdminApp> app, string name) =>
        app.FindAll("main section.cms-card").Single(section => section.QuerySelector("h2")!.TextContent == name);

    private static JsonArray Array(string[]? items) => new([.. (items ?? []).Select(item => (JsonNode)item)]);

    private string Slug(Guid site) => (string)Management.Sites.Single(item => (Guid)item!["id"]! == site)!["slug"]!;

    private HttpResponseMessage? Answer(HttpRequestMessage request, string path, string? body)
    {
        var site = request.Headers.TryGetValues("X-Cms-Site", out var values) && Guid.TryParse(values.Single(), out var id) ? id : MainSite;
        var segments = path.Split('/').Select(Uri.UnescapeDataString).ToArray();
        switch (request.Method.Method, segments)
        {
            case ("GET", ["plugins"]):
                return Json(new JsonObject
                {
                    ["items"] = new JsonArray([.. Installed.Select(plugin =>
                    {
                        var item = plugin.DeepClone().AsObject();
                        var name = (string)plugin["name"]!;
                        item["enabled"] = !Disabled.Contains((name, site));
                        item["sites"] = new JsonArray([.. Managed.Select(managed => (JsonNode)new JsonObject
                        {
                            ["siteId"] = managed,
                            ["slug"] = Slug(managed),
                            ["enabled"] = !Disabled.Contains((name, managed)),
                        })]);
                        return (JsonNode)item;
                    })]),
                });
            case ("PUT", ["plugins", var name, "sites", var target]):
                if (Installed.All(plugin => (string)plugin["name"]! != name))
                {
                    return Problem(HttpStatusCode.NotFound, "FCMS1551", "plugin");
                }

                var switched = Guid.Parse(target);
                var enabled = (bool)JsonNode.Parse(body!)!["enabled"]!;
                if (enabled)
                {
                    Disabled.Remove((name, switched));
                }
                else
                {
                    Disabled.Add((name, switched));
                }

                return Json(new JsonObject { ["siteId"] = switched, ["slug"] = Slug(switched), ["enabled"] = enabled });
            default:
                return null;
        }
    }
}
