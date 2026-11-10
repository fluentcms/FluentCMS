using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Agents;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Plugins.Sample;
using FluentCMS.Providers;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Mcp.Tests.McpTesting;
using static FluentCMS.Plugins.Mcp.Tests.McpToolsTesting;

namespace FluentCMS.Plugins.Mcp.Tests;

// The tool list of spec 5.4: the tools of each exposed type and the shared ones, filtered by what the caller may do.
public sealed class McpContentToolListTests(McpToolsFixture fixture) : IClassFixture<McpToolsFixture>
{
    private static readonly string[] SevenTools = ["post_create_draft", "post_delete", "post_discard_draft", "post_get", "post_list", "post_search", "post_update_draft"];

    [Fact]
    public async Task APrincipalThatMayReadAndWriteATypeListsItsSevenTools()
    {
        await using var host = Host(Path.GetTempPath(), new FakeDownloads()).Configure(cms => cms.Providers.Use<ISearchIndex, NoHits>());
        using var http = await RoleAsync(host, Grant("content.post.read"), Grant("content.post.create"), Grant("content.post.update"), Grant("content.post.delete"));
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        var tools = await ToolsAsync(client);

        Of(tools, "post_").ShouldBe(SevenTools);
        Of(tools, "writer_").ShouldBeEmpty();
        tools.Keys.ShouldContain(McpContentTools.Query);
        tools.Keys.ShouldNotContain(McpContentTools.Publish);
        (await OkAsync(client, "post_search", new { q = "harbour" })).GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task APrincipalThatMayOnlyReadATypeListsItsListAndGetToolsOnly()
    {
        using var http = await RoleAsync(fixture.Host, Grant("content.post.read"));
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        var tools = await ToolsAsync(client);

        Of(tools, "post_").ShouldBe(["post_get", "post_list"]);
        tools.Keys.Where(name => !name.StartsWith("post_", StringComparison.Ordinal) && !name.StartsWith("sample_", StringComparison.Ordinal)).ShouldBe([McpContentTools.Query]);
    }

    // Core refuses q= without a search index, so no tool offers it.
    [Fact]
    public async Task WithoutASearchIndexNoSearchToolIsListed()
    {
        using var http = await KeyAsync(fixture.Host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        var tools = await ToolsAsync(client);

        Of(tools, "post_").ShouldBe(SevenTools.Where(name => name != "post_search"));
        tools[McpContentTools.Query].InputSchema.GetProperty("properties").TryGetProperty("q", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task ASingletonHasItsGetUpdateAndDiscardToolsOnly()
    {
        using var http = await KeyAsync(fixture.Host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        var tools = await ToolsAsync(client);

        Of(tools, "masthead_").ShouldBe(["masthead_discard_draft", "masthead_get", "masthead_update_draft"]);
        tools["masthead_get"].InputSchema.GetProperty("properties").TryGetProperty("id", out _).ShouldBeFalse();
        tools["masthead_update_draft"].InputSchema.GetProperty("required").EnumerateArray().Select(name => name.GetString()).ShouldBe(["expectedVersion", "fields"]);
    }

    [Fact]
    public async Task ThePublishToolsAreListedForThoseWhoMayPublishAndNameTheirTypes()
    {
        using var http = await RoleAsync(fixture.Host, Grant("content.*.read"), Grant("content.post.publish"), Grant("content.writer.unpublish"));
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        var tools = await ToolsAsync(client);

        Types(tools[McpContentTools.Publish].InputSchema).ShouldBe(["post"]);
        Types(tools[McpContentTools.Unpublish].InputSchema).ShouldBe(["writer"]);
        Types(tools[McpContentTools.Schedule].InputSchema).ShouldBe(["post", "writer"]);
        Types(tools[McpContentTools.Query].InputSchema).ShouldBe(["masthead", "post", "writer"]);
    }

    // A Delivery key reaches /mcp, reads what is published and manages nothing, so it is offered the read tools alone.
    [Fact]
    public async Task ADeliveryKeyListsTheReadToolsOnly()
    {
        var host = fixture.Host;
        using var http = host.AsKey(await host.CreateApiKeyAsync(ApiKeyClass.Delivery, BuiltInRoles.EditorName, cancellationToken: Token));
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        var tools = await ToolsAsync(client);

        Of(tools, "post_").ShouldBe(["post_get", "post_list"]);
        Of(tools, "masthead_").ShouldBe(["masthead_get"]);
        Of(tools, "media_").ShouldBeEmpty();
        tools.Keys.ShouldNotContain(McpContentTools.Publish);
        tools["post_list"].InputSchema.GetProperty("properties").GetProperty("status").GetProperty("enum").EnumerateArray().Select(status => status.GetString()).ShouldBe(["published"]);
    }

    [Fact]
    public async Task EveryToolCarriesItsReadOnlyOrDestructiveHint()
    {
        using var http = await KeyAsync(fixture.Host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        var tools = await ToolsAsync(client);

        foreach (var name in (string[])["post_list", "post_get", "masthead_get", McpContentTools.Query, McpMediaTools.List, McpMediaTools.Search, McpMediaTools.Get])
        {
            tools[name].Annotations.ShouldNotBeNull().ShouldSatisfyAllConditions(
                hints => hints.ReadOnlyHint.ShouldBe(true, name),
                hints => hints.DestructiveHint.ShouldBe(false, name));
        }

        foreach (var name in (string[])["post_create_draft", McpMediaTools.Upload])
        {
            tools[name].Annotations.ShouldNotBeNull().ShouldSatisfyAllConditions(
                hints => hints.ReadOnlyHint.ShouldBe(false, name),
                hints => hints.DestructiveHint.ShouldBe(false, name));
        }

        foreach (var name in (string[])["post_update_draft", "post_discard_draft", "post_delete", McpContentTools.Publish, McpContentTools.Unpublish, McpContentTools.Schedule, McpMediaTools.Update])
        {
            tools[name].Annotations.ShouldNotBeNull().ShouldSatisfyAllConditions(
                hints => hints.ReadOnlyHint.ShouldBe(false, name),
                hints => hints.DestructiveHint.ShouldBe(true, name));
        }

        tools[McpMediaTools.Upload].Annotations!.OpenWorldHint.ShouldBe(true);
        tools["post_list"].Title.ShouldBe("List post");
    }

    // The meeting point with the agent instructions: a tool of a type names it, and a shared tool names none.
    [Fact]
    public async Task AToolOfATypeNamesItsContentType()
    {
        var host = fixture.Host;
        var admin = host.As(await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token), Origin.Mcp);

        var tools = await admin.RunAsync<IServiceProvider, IReadOnlyList<McpToolDescriptor>>(async services =>
        {
            var request = new McpListRequest(services.GetRequiredService<IOperationContextAccessor>().Current!);
            return await services.GetRequiredService<McpContentTools>().ListToolsAsync(request, Token);
        });

        tools.Where(tool => tool.Name.StartsWith("post_", StringComparison.Ordinal)).ShouldAllBe(tool => tool.ContentType == "post");
        tools.Single(tool => tool.Name == "masthead_get").ContentType.ShouldBe("masthead");
        tools.Where(tool => tool.Name is McpContentTools.Publish or McpContentTools.Unpublish or McpContentTools.Schedule or McpContentTools.Query).ShouldAllBe(tool => tool.ContentType == null);
    }

    // The types come in the order of the schema, which is by name: masthead, post, writer.
    [Fact]
    public async Task ATypeOverTheCapHasNoToolsOfItsOwnIsReachedThroughQueryAndIsNamedInTheLogOnce()
    {
        var logs = new LogCapture();
        await using var host = Host(Path.GetTempPath(), new FakeDownloads()).WithServices(services =>
        {
            services.AddSingleton<ILoggerProvider>(logs);
            services.Configure<McpOptions>(options => options.MaxExposedTypes = 1);
        });
        using var http = await KeyAsync(host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        var writer = await OkAsync(client, McpContentTools.Query, new { type = "writer" });

        var tools = await ToolsAsync(client);
        await ToolsAsync(client);

        Of(tools, "masthead_").ShouldNotBeEmpty();
        Of(tools, "post_").ShouldBeEmpty();
        Of(tools, "writer_").ShouldBeEmpty();
        Types(tools[McpContentTools.Query].InputSchema).ShouldBe(["masthead", "post", "writer"]);
        writer.GetProperty("items").GetArrayLength().ShouldBe(0);
        logs.Lines.Where(line => line.Id == 9288).Select(line => line.Level).ShouldBe([LogLevel.Warning, LogLevel.Warning]);
        logs.Lines.Where(line => line.Id == 9288).ShouldContain(line => line.Message.Contains("writer", StringComparison.Ordinal));
        logs.Lines.Where(line => line.Id == 9288).ShouldContain(line => line.Message.Contains("post", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OnlyTheExposedTypesHaveToolsAndAnotherTypeIsNotAvailable()
    {
        await using var host = Host(Path.GetTempPath(), new FakeDownloads()).WithServices(services => services.Configure<McpOptions>(options => options.ExposedTypes = ["writer"]));
        using var http = await KeyAsync(host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        var tools = await ToolsAsync(client);
        var hidden = await CallAsync(client, McpContentTools.Query, new { type = "post" });
        var missing = await CallAsync(client, McpContentTools.Publish, new { type = "nothing", id = Guid.NewGuid() });

        Of(tools, "post_").ShouldBeEmpty();
        Of(tools, "writer_").ShouldNotBeEmpty();
        Types(tools[McpContentTools.Query].InputSchema).ShouldBe(["writer"]);
        Code(hidden).ShouldBe("FCMS1612");
        Code(missing).ShouldBe("FCMS1612");
        (await Should.ThrowAsync<McpProtocolException>(async () => await CallAsync(client, "post_list"))).Message.ShouldContain("FCMS1600");
    }

    [Fact]
    public async Task AnExposedTypeTheSchemaDoesNotHaveStopsTheHost()
    {
        await using var host = Host(Path.GetTempPath(), new FakeDownloads()).WithServices(services => services.Configure<McpOptions>(options => options.ExposedTypes = ["pots"]));

        Should.Throw<OptionsValidationException>(() => host.Services).Message.ShouldContain("ExposedTypes");
    }

    [Fact]
    public async Task ThePluginListNamesTheToolsOfEachPluginForTheCaller()
    {
        using var admin = await KeyAsync(fixture.Host);
        using var reader = await RoleAsync(fixture.Host, Grant("content.post.read"));

        var forAdmin = await ToolsOfAsync(admin);
        var forReader = await ToolsOfAsync(reader);

        forAdmin["mcp"].ShouldContain("post_create_draft");
        forAdmin["mcp"].ShouldContain(McpContentTools.Publish);
        forAdmin["mcp"].ShouldContain(McpMediaTools.Upload);
        forAdmin["sample"].ShouldContain(SampleTools.WhoAmI);
        forAdmin["media"].ShouldBeEmpty();
        forReader["mcp"].ShouldBe(["post_list", "post_get", McpContentTools.Query]);
    }

    private static async Task<Dictionary<string, List<string>>> ToolsOfAsync(HttpClient client)
    {
        var list = await client.GetFromJsonAsync<JsonNode>(new Uri("/api/management/v1/plugins", UriKind.Relative), Token);
        return list!["items"]!.AsArray().ToDictionary(
            plugin => plugin!["name"]!.GetValue<string>(),
            plugin => plugin!["contributions"]!["tools"]!.AsArray().Select(tool => tool!.GetValue<string>()).ToList(),
            StringComparer.Ordinal);
    }

    private static List<string?> Types(JsonElement schema) =>
        [.. schema.GetProperty("properties").GetProperty("type").GetProperty("enum").EnumerateArray().Select(type => type.GetString())];

    public sealed class NoHits : ISearchIndex
    {
        public Task<IReadOnlyList<Guid>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Guid>>([]);
    }
}
