using System.Net;
using FluentCMS.Core.Sites;
using FluentCMS.Plugins.Sample;
using ModelContextProtocol;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Mcp.Tests.McpTesting;

namespace FluentCMS.Plugins.Mcp.Tests;

// Per-site enablement: the server itself, and the providers of another plugin. Each test turns a plugin off in a site of
// its own.
public sealed class McpSiteTests(McpFixture fixture) : IClassFixture<McpFixture>
{
    [Fact]
    public async Task TheToolsResourcesAndPromptsOfAPluginAreGoneForASiteWhereThatPluginIsDisabled()
    {
        var host = fixture.Host;
        var docs = await CreateSiteAsync(host);
        using var http = await KeyAsync(host);
        await SetEnabledAsync(host, "sample", docs, enabled: false);
        await using var inDocs = await ConnectAsync(http, docs, InitializeProtocol);
        await using var inDefault = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        (await inDocs.ListToolsAsync(cancellationToken: Token)).ShouldBeEmpty();
        (await inDocs.ListResourcesAsync(cancellationToken: Token)).ShouldBeEmpty();
        (await inDocs.ListResourceTemplatesAsync(cancellationToken: Token)).ShouldBeEmpty();
        (await inDocs.ListPromptsAsync(cancellationToken: Token)).ShouldBeEmpty();
        (await Should.ThrowAsync<McpProtocolException>(async () => await inDocs.CallToolAsync(SampleTools.WhoAmI, cancellationToken: Token))).Message.ShouldContain("FCMS1600");
        (await Should.ThrowAsync<McpProtocolException>(async () => await inDocs.ReadResourceAsync(SampleResources.Greeting, cancellationToken: Token))).Message.ShouldContain("FCMS1601");
        (await Should.ThrowAsync<McpProtocolException>(async () => await inDocs.GetPromptAsync(SamplePrompts.Greet, new Dictionary<string, object?> { ["name"] = "Ada" }, cancellationToken: Token))).Message.ShouldContain("FCMS1602");

        (await inDefault.ListToolsAsync(cancellationToken: Token)).ShouldNotBeEmpty();
        (await inDefault.CallToolAsync(SampleTools.WhoAmI, cancellationToken: Token)).IsError.ShouldBe(false);
        (await inDefault.ListResourcesAsync(cancellationToken: Token)).ShouldNotBeEmpty();
        (await inDefault.ListPromptsAsync(cancellationToken: Token)).ShouldNotBeEmpty();

        await SetEnabledAsync(host, "sample", docs, enabled: true);
        (await inDocs.ListToolsAsync(cancellationToken: Token)).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task TheServerAnswers404ForASiteWhereTheMcpPluginIsDisabledAndKeepsServingAnother()
    {
        var host = fixture.Host;
        var docs = await CreateSiteAsync(host);
        using var http = await KeyAsync(host);
        using var anonymous = host.CreateClient();
        await SetEnabledAsync(host, "mcp", docs, enabled: false);

        using var disabled = await PostAsync(http, docs);
        using var unsigned = await PostAsync(anonymous, docs);
        using var elsewhere = await PostAsync(http, DefaultSite.Id);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        disabled.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(disabled)).ShouldBe("FCMS1550");

        // The caller is checked first, so an anonymous caller learns nothing of where the plugin is off.
        unsigned.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        elsewhere.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.ListToolsAsync(cancellationToken: Token)).ShouldNotBeEmpty();
        await Should.ThrowAsync<Exception>(async () => await ConnectAsync(http, docs, InitializeProtocol));

        await SetEnabledAsync(host, "mcp", docs, enabled: true);
        using var enabled = await PostAsync(http, docs);
        enabled.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
