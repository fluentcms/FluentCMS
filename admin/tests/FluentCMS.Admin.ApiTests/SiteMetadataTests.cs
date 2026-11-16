using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;
using FluentCMS.Core.Authorization;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.ApiTests;

public sealed class SiteMetadataTests
{
    private static CancellationToken Token => AdminHost.Token;

    [Fact]
    public async Task TheSchemaAndPluginsAreReadOncePerSiteAndAgainAfterASiteSwitch()
    {
        await using var admin = new AdminHost();
        using var client = await admin.SignInAsync(BuiltInRoles.SuperAdminName);
        var second = new JsonObject { ["name"] = "Second", ["slug"] = "second", ["defaultLocale"] = "en" };
        await client.Api.SendAsync(new ApiRequest(HttpMethod.Post, "sites") { Body = second }, Token);

        client.Site = "default";
        var first = await ReadTwiceAsync(client);
        client.Site = "second";
        var other = await ReadTwiceAsync(client);
        client.Site = "default";
        await ReadTwiceAsync(client);

        client.Counts["GET /api/management/v1/schema default"].ShouldBe(2);
        client.Counts["GET /api/management/v1/plugins default"].ShouldBe(2);
        client.Counts["GET /api/management/v1/schema second"].ShouldBe(1);
        client.Counts["GET /api/management/v1/plugins second"].ShouldBe(1);
        first.Schema.Types.Select(type => type.Name).ShouldContain("article");
        other.Plugins.Select(plugin => plugin.Name).ShouldContain("media");
    }

    // The second site has only en. The site switches while the view's locale is still fr, as the shell switches site first.
    [Fact]
    public async Task AStaleLocaleFailsNoneOfTheReadsThatFollowASiteSwitch()
    {
        await using var admin = new AdminHost();
        using var client = await admin.SignInAsync(BuiltInRoles.SuperAdminName);
        var second = new JsonObject { ["name"] = "Second", ["slug"] = "second", ["defaultLocale"] = "en" };
        await client.Api.SendAsync(new ApiRequest(HttpMethod.Post, "sites") { Body = second }, Token);
        client.Locale = "fr";
        await client.Metadata.GetSchemaAsync(Token);

        client.Site = "second";
        var schema = await client.Metadata.GetSchemaAsync(Token);
        var plugins = await client.Metadata.GetPluginsAsync(Token);
        var me = await client.Client.GetMeAsync(Token);
        var sites = await client.Client.GetMySitesAsync(Token);
        var refused = await Should.ThrowAsync<ApiProblemException>(() => client.Client.ListEntriesAsync("article", null, Token));

        schema.Types.ShouldNotBeEmpty();
        plugins.ShouldNotBeEmpty();
        me.Kind.ShouldBe("adminUser");
        sites.Single(site => site.Slug == "second").Locales.Select(locale => locale.Code).ShouldBe(["en"]);
        (refused.Problem.Status, refused.Problem.Code).ShouldBe((422, "FCMS2312"));
    }

    // A site that was deleted or renamed fails every call that names it, but not the caller's own reads, which the shell
    // needs to recover.
    [Fact]
    public async Task TheCallersOwnReadsDoNotSendASiteThatIsGone()
    {
        await using var admin = new AdminHost();
        using var client = await admin.SignInAsync(BuiltInRoles.SuperAdminName);
        client.Site = "gone";
        client.Locale = "de";

        var me = await client.Client.GetMeAsync(Token);
        var sites = await client.Client.GetMySitesAsync(Token);
        var refused = await Should.ThrowAsync<ApiProblemException>(() => client.Metadata.GetSchemaAsync(Token));

        me.Kind.ShouldBe("adminUser");
        sites.ShouldHaveSingleItem().Slug.ShouldBe("default");
        (refused.Problem.Status, refused.Problem.Code).ShouldBe((404, "FCMS2301"));
    }

    private static async Task<(SchemaDescription Schema, IReadOnlyList<PluginInfo> Plugins)> ReadTwiceAsync(AdminClient client)
    {
        var schema = await client.Metadata.GetSchemaAsync(Token);
        var plugins = await client.Metadata.GetPluginsAsync(Token);
        (await client.Metadata.GetSchemaAsync(Token)).ShouldBeSameAs(schema);
        (await client.Metadata.GetPluginsAsync(Token)).ShouldBeSameAs(plugins);
        return (schema, plugins);
    }
}
