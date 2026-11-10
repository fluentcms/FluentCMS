using System.Net;
using FluentCMS.Core.Authorization;
using FluentCMS.Plugins.Sample;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Mcp.Tests.McpTesting;

namespace FluentCMS.Plugins.Mcp.Tests;

// The server of #2977 as the protocol's own client sees it: who may connect, and what the handshake answers.
public sealed class McpServerTests(McpFixture fixture) : IClassFixture<McpFixture>
{
    [Fact]
    public async Task ABearerTokenWithTheMcpScopeInitializesAndListsTools()
    {
        var (http, _) = await UserAsync(fixture.Host);
        using (http)
        {
            await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

            client.ServerInfo!.Name.ShouldBe("fluentcms");
            client.ServerInfo!.Title.ShouldBe("FluentCMS");
            client.ServerInfo!.Version.ShouldBe(McpServerSetup.Version);
            client.NegotiatedProtocolVersion.ShouldBe(InitializeProtocol);
            client.ServerCapabilities.Tools.ShouldNotBeNull();
            client.ServerCapabilities.Resources.ShouldNotBeNull();
            client.ServerCapabilities.Prompts.ShouldNotBeNull();
            await client.PingAsync(cancellationToken: Token);
            (await client.ListToolsAsync(cancellationToken: Token)).Select(tool => tool.Name).ShouldContain(SampleTools.WhoAmI);
        }
    }

    [Fact]
    public async Task AManagementKeyInitializesAndListsTools()
    {
        using var http = await KeyAsync(fixture.Host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        client.ServerInfo!.Name.ShouldBe("fluentcms");
        await client.PingAsync(cancellationToken: Token);
        (await client.ListToolsAsync(cancellationToken: Token)).Select(tool => tool.Name).Where(name => name.StartsWith("sample_", StringComparison.Ordinal))
            .ShouldBe([SampleTools.WhoAmI, SampleTools.EntryCreate, SampleTools.EntryTitles, SampleTools.NoteAdd]);
    }

    // A client of the newest revision sends no initialize: it discovers the server and then calls it, each request on its own.
    [Fact]
    public async Task AClientOfTheNewestRevisionConnectsWithoutASession()
    {
        using var http = await KeyAsync(fixture.Host);
        await using var client = await ConnectAsync(http);

        client.SessionId.ShouldBeNull();
        client.ServerCapabilities.Tools.ShouldNotBeNull();
        (await client.ListToolsAsync(cancellationToken: Token)).ShouldNotBeEmpty();
    }

    // Stateless: no stream to open and no session to end, and no legacy SSE endpoint beside it.
    [Theory]
    [InlineData("GET", "/mcp")]
    [InlineData("DELETE", "/mcp")]
    [InlineData("GET", "/mcp/sse")]
    [InlineData("POST", "/mcp/message")]
    public async Task OnlyPostIsServed(string method, string path)
    {
        using var http = await KeyAsync(fixture.Host);
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));
        request.Headers.Accept.ParseAdd("text/event-stream");

        using var response = await http.SendAsync(request, Token);

        response.StatusCode.ShouldBeOneOf(HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TheServerNamesItselfFromItsOptions()
    {
        await using var host = Host().WithServices(services => services.Configure<McpOptions>(options => (options.Name, options.Title) = ("docs-cms", "Docs")));
        using var http = await KeyAsync(host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        client.ServerInfo!.Name.ShouldBe("docs-cms");
        client.ServerInfo!.Title.ShouldBe("Docs");
    }

    [Fact]
    public async Task OptionsThatNameNothingStopTheHost()
    {
        await using var host = Host().WithServices(services => services.Configure<McpOptions>(options => options.Name = "two words"));

        Should.Throw<Microsoft.Extensions.Options.OptionsValidationException>(() => host.Services).Message.ShouldContain(McpOptions.Section);
    }

    [Fact]
    public async Task ThePermissionToManageMcpIsInTheCatalogue()
    {
        using var http = await KeyAsync(fixture.Host);

        using var response = await http.GetAsync(new Uri("/api/management/v1/permissions", UriKind.Relative), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Token)).ShouldContain($"\"{McpPermissions.Manage}\"");
        fixture.Host.Services.GetRequiredService<PermissionCatalogue>().Find(McpPermissions.Manage).ShouldNotBeNull();
    }

    [Fact]
    public async Task AnUnknownToolResourceOrPromptIsAProtocolErrorWithItsCode()
    {
        using var http = await KeyAsync(fixture.Host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        var tool = await Should.ThrowAsync<McpProtocolException>(async () => await client.CallToolAsync("no_such_tool", cancellationToken: Token));
        var resource = await Should.ThrowAsync<McpProtocolException>(async () => await client.ReadResourceAsync("sample://nothing", cancellationToken: Token));
        var prompt = await Should.ThrowAsync<McpProtocolException>(async () => await client.GetPromptAsync("no_such_prompt", cancellationToken: Token));

        tool.ErrorCode.ShouldBe(McpErrorCode.InvalidParams);
        tool.Message.ShouldContain("FCMS1600");
        resource.ErrorCode.ShouldBe(McpErrorCode.ResourceNotFound);
        resource.Message.ShouldContain("FCMS1601");
        prompt.ErrorCode.ShouldBe(McpErrorCode.InvalidParams);
        prompt.Message.ShouldContain("FCMS1602");
    }
}
