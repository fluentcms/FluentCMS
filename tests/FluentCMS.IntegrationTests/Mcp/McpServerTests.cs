using System.Net;
using System.Text.Json;
using FluentCMS.Infrastructure.Persistence;
using FluentCMS.Mcp;
using ModelContextProtocol.Client;

namespace FluentCMS.IntegrationTests.Mcp;

public sealed class McpServerTests
{
    // 2026-07-28 clients send server/discover; 2025-11-25 and earlier ones the initialize handshake.
    [Theory]
    [InlineData("2026-07-28")]
    [InlineData("2025-11-25")]
    public async Task A_client_connects_over_http_at_mcp_and_gets_no_session(string protocolVersion)
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        var issued = await McpAgents.IssueAsync(host, await host.CreateFirstAdministratorAsync());

        await using var client = await McpAgents.ConnectOverHttpAsync(host, issued.Key, protocolVersion);
        var stream = await host.Client.GetAsync(McpEndpoints.Route);

        Assert.Equal(protocolVersion, client.NegotiatedProtocolVersion);
        AssertFluentCmsServer(client);
        Assert.Equal(McpAgents.BuiltInTools, await McpAgents.ToolNamesAsync(client));
        Assert.Null(client.SessionId);
        // A stateless server offers no stream to GET.
        Assert.Equal(HttpStatusCode.MethodNotAllowed, stream.StatusCode);
        Assert.Equal(["POST"], stream.Content.Headers.Allow);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Fluentcms_mcp_serves_stdio_on_the_database_the_web_host_uses(string provider)
    {
        using var install = new TemporaryInstall();
        await using var database = await TemporaryDatabase.CreateAsync(provider);
        // Issuing the key migrates the database first; HostTests shows the stdio host migrates on its own.
        var issued = await McpAgents.IssueAsync(database.Configuration);
        await using var stdio = StdioHost.Start(new Dictionary<string, string?>(database.Configuration)
        {
            ["DataProtection:KeysDirectory"] = install.KeysDirectory,
            [StdioAgentAuthentication.KeyVariable] = issued.Key,
        });

        await using (var client = await stdio.ConnectAsync())
        {
            await using var web = await AdminHost.StartAsync(install, settings: database.Configuration);
            await using var http = await McpAgents.ConnectOverHttpAsync(web, issued.Key);

            AssertFluentCmsServer(client);
            Assert.Equal(http.ServerInfo.Version, client.ServerInfo.Version);
            Assert.Equal(McpAgents.BuiltInTools, await McpAgents.ToolNamesAsync(client));
        }

        Assert.Equal(0, await stdio.CloseInputAndWaitForExitAsync());
        // Standard output carries the protocol, so the host's log lines are all on standard error.
        Assert.Contains(stdio.ErrorLines, IsLogLine);
        Assert.DoesNotContain(stdio.ErrorLines, line => line.Contains(issued.Key, StringComparison.Ordinal));
    }

    private static void AssertFluentCmsServer(McpClient client)
    {
        Assert.Equal(McpServiceCollectionExtensions.ServerName, client.ServerInfo.Name);
        Assert.Equal(typeof(McpServiceCollectionExtensions).Assembly.GetName().Version!.ToString(3), client.ServerInfo.Version);
        Assert.NotNull(client.ServerCapabilities.Tools);
    }

    private static bool IsLogLine(string line)
    {
        try
        {
            using var entry = JsonDocument.Parse(line);
            return entry.RootElement.TryGetProperty("Category", out _) && entry.RootElement.TryGetProperty("LogLevel", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
