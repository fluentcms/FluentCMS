using System.Text.Json.Nodes;
using FluentCMS.Admin.Administration.Plugins;
using FluentCMS.Admin.Api;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Sites;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.ApiTests.Administration;

// The records of the plugins area held to the reference host's OpenAPI document (spec 7), and its calls on the test host: a
// plugin disabled in a site and enabled again, which the list and the shell's metadata follow, and the refusals of a caller
// who does not manage plugins in the site it switches.
public sealed class PluginsContractTests
{
    private const string Prefix = "/api/management/v1/";

    private static CancellationToken Token => AdminHost.Token;

    [Fact]
    public async Task TheRecordsOfThePluginsAreaMatchTheOpenApiDocument()
    {
        var document = await OpenApiDocument.ReadAsync();
        var paths = document["paths"]!;
        var records = PluginsJsonContext.Default;
        List<string> gaps = [];

        void Check(System.Text.Json.Serialization.Metadata.JsonTypeInfo record, string path, string method) =>
            OpenApiContractTests.Check(record, OpenApiContractTests.Answer(paths, Prefix + path, method), $"{path} {method}", document, gaps);

        Check(records.ItemListPluginInfo, "plugins", "get");
        Check(records.PluginSiteInfo, "plugins/{name}/sites/{site}", "put");

        gaps.ShouldBeEmpty();
        Body(document, "plugins/{name}/sites/{site}", "put").Select(member => member.Key).ShouldBe(["enabled"]);
    }

    [Fact]
    public async Task APluginIsDisabledInASiteAndEnabledAgain()
    {
        await using var admin = new AdminHost();
        using var client = await admin.SignInAsync(BuiltInRoles.SuperAdminName);
        var api = client.Api;

        var search = (await api.GetPluginsAsync(Token)).Single(plugin => plugin.Name == "search");
        (search.Enabled, search.Sites.Single(site => site.SiteId == DefaultSite.Id).Enabled).ShouldBe((true, true));
        search.Contributions.Jobs.ShouldContain("search.rebuild");

        var disabled = await api.SetPluginEnabledAsync("search", DefaultSite.Id, enabled: false, Token);
        (disabled.SiteId, disabled.Enabled).ShouldBe((DefaultSite.Id, false));
        search = (await api.GetPluginsAsync(Token)).Single(plugin => plugin.Name == "search");
        (search.Enabled, search.Sites.Single(site => site.SiteId == DefaultSite.Id).Enabled).ShouldBe((false, false));

        (await api.SetPluginEnabledAsync("search", DefaultSite.Id, enabled: true, Token)).Enabled.ShouldBeTrue();
        (await api.GetPluginsAsync(Token)).Single(plugin => plugin.Name == "search").Enabled.ShouldBeTrue();

        var unknown = await Should.ThrowAsync<ApiProblemException>(() => api.SetPluginEnabledAsync("no-such-plugin", DefaultSite.Id, enabled: false, Token));
        PluginProblems.Has(unknown, PluginProblems.PluginNotFound).ShouldBeTrue();
    }

    // The editor's role holds no plugins.manage, so the list names no site to switch and a switch is refused.
    [Fact]
    public async Task ACallerWhoDoesNotManagePluginsInTheSiteIsRefusedTheSwitch()
    {
        await using var admin = new AdminHost();
        using var super = await admin.SignInAsync(BuiltInRoles.SuperAdminName);
        var role = new JsonObject
        {
            ["name"] = "Plugin reader",
            ["siteId"] = DefaultSite.Id,
            ["grants"] = new JsonArray(new JsonObject { ["permission"] = CorePermissions.SchemaRead }),
        };
        await super.Api.SendAsync(new ApiRequest(HttpMethod.Post, "roles") { Body = role }, Token);
        using var client = await admin.SignInAsync("Plugin reader");

        (await client.Api.GetPluginsAsync(Token)).ShouldAllBe(plugin => plugin.Sites.Count == 0);
        var refused = await Should.ThrowAsync<ApiProblemException>(() => client.Api.SetPluginEnabledAsync("search", DefaultSite.Id, enabled: false, Token));
        PluginProblems.IsForbidden(refused).ShouldBeTrue();
    }

    private static JsonObject Body(JsonObject document, string path, string method)
    {
        var schema = document["paths"]![Prefix + path]![method]!["requestBody"]!["content"]!["application/json"]!["schema"]!;
        var reference = schema["$ref"]?.GetValue<string>();
        var resolved = reference is null ? schema : document["components"]!["schemas"]![reference.Split('/')[^1]]!;
        return resolved["properties"]!.AsObject();
    }
}
