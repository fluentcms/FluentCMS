using System.Text.Json.Nodes;
using FluentCMS.Core.Authorization;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Mcp.Tests.McpSiteTesting;
using static FluentCMS.Plugins.Mcp.Tests.McpTesting;

namespace FluentCMS.Plugins.Mcp.Tests;

// The schema resources of #2980: what ISchemaIntrospection gives the caller, and nothing else.
public sealed class McpSchemaResourceTests(McpSiteFixture fixture) : IClassFixture<McpSiteFixture>
{
    [Fact]
    public async Task TheSchemaAndItsTemplatesAreListedAndReadAsTheManagementApiServesThem()
    {
        using var http = await KeyAsync(fixture.Host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        var resources = await client.ListResourcesAsync(cancellationToken: Token);
        var templates = await client.ListResourceTemplatesAsync(cancellationToken: Token);
        var schema = JsonNode.Parse(await ReadAsync(client, McpSiteResources.Schema))!;
        var page = JsonNode.Parse(await ReadAsync(client, "fluentcms://schema/page"))!;
        using var served = await http.GetAsync(new Uri("/api/management/v1/schema", UriKind.Relative), Token);

        resources.Single(resource => resource.Uri == McpSiteResources.Schema).MimeType.ShouldBe("application/json");
        templates.Select(template => template.UriTemplate).ShouldContain(McpSiteResources.SchemaTypeTemplate);
        templates.Select(template => template.UriTemplate).ShouldContain(McpSiteResources.InstructionsTemplate);
        Names(schema["types"]!).ShouldBe(["article", "page"]);
        Names(page["fields"]!).ShouldBe(["title", "notes"]);
        JsonNode.DeepEquals(schema, JsonNode.Parse(await served.Content.ReadAsStringAsync(Token))).ShouldBeTrue();
        JsonNode.DeepEquals(page, schema["types"]![1]).ShouldBeTrue();
    }

    [Fact]
    public async Task ATypeTheCallerMayNotReadIsLeftOutAndUnknownAndAFieldItMayNotReadIsLeftOut()
    {
        var host = fixture.Host;
        var role = await CreateRoleAsync(host, ("schema.read", null), ("content.page.read", new PermissionConstraints { Fields = ["title"] }));
        using var http = await KeyAsync(host, role: role);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        var schema = await ReadAsync(client, McpSiteResources.Schema);
        var page = JsonNode.Parse(await ReadAsync(client, "fluentcms://schema/page"))!;
        var unreadable = await RefusalAsync(client, "fluentcms://schema/article");
        var unknown = await RefusalAsync(client, "fluentcms://schema/nothing");

        Names(JsonNode.Parse(schema)!["types"]!).ShouldBe(["page"]);
        schema.ShouldNotContain("article");
        schema.ShouldNotContain("notes");
        Names(page["fields"]!).ShouldBe(["title"]);
        unreadable.ShouldContain(McpErrorCodes.UnknownResource);
        unreadable.Replace("article", "nothing", StringComparison.Ordinal).ShouldBe(unknown);
    }

    // The service refuses the schema to a caller without schema.read, so the server does not list it either.
    [Fact]
    public async Task ACallerWithoutSchemaReadIsNotListedTheSchemaAndIsRefusedIt()
    {
        var host = fixture.Host;
        using var http = await KeyAsync(host, role: await CreateRoleAsync(host, ("content.article.read", null)));
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        var resources = await client.ListResourcesAsync(cancellationToken: Token);
        var templates = await client.ListResourceTemplatesAsync(cancellationToken: Token);
        var schema = await RefusalAsync(client, McpSiteResources.Schema);
        var type = await RefusalAsync(client, "fluentcms://schema/article");

        resources.ShouldAllBe(resource => !resource.Uri.StartsWith("fluentcms://", StringComparison.Ordinal));
        templates.ShouldAllBe(template => !template.UriTemplate.StartsWith("fluentcms://", StringComparison.Ordinal));
        schema.ShouldContain("FCMS3103");
        type.ShouldContain("FCMS3103");
    }

    private static List<string> Names(JsonNode array) => [.. array.AsArray().Select(item => item!["name"]!.GetValue<string>())];
}
