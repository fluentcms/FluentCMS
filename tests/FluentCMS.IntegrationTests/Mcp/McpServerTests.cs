using System.Net;
using System.Text.Json;
using FluentCMS.Infrastructure.Persistence;
using FluentCMS.Mcp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

        await using var client = await ConnectOverHttpAsync(host, protocolVersion);
        var stream = await host.Client.GetAsync(McpEndpoints.Route);

        Assert.Equal(protocolVersion, client.NegotiatedProtocolVersion);
        AssertFluentCmsServer(client);
        Assert.Empty(await client.ListToolsAsync());
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
        await using var stdio = StdioHost.Start(new Dictionary<string, string?>(database.Configuration)
        {
            ["DataProtection:KeysDirectory"] = install.KeysDirectory,
        });

        await using (var client = await stdio.ConnectAsync())
        {
            // The session starts after the host has migrated the database it was given, the one the web host then opens.
            Assert.Empty(await PendingMigrationsAsync(database.Configuration));

            await using var web = await AdminHost.StartAsync(install, settings: database.Configuration);
            await using var http = await ConnectOverHttpAsync(web);

            AssertFluentCmsServer(client);
            Assert.Equal(http.ServerInfo.Version, client.ServerInfo.Version);
            Assert.Empty(await client.ListToolsAsync());
        }

        Assert.Equal(0, await stdio.CloseInputAndWaitForExitAsync());
        // Standard output carries the protocol, so the host's log lines are all on standard error.
        Assert.Contains(stdio.ErrorLines, IsLogLine);
    }

    private static async Task<McpClient> ConnectOverHttpAsync(AdminHost host, string? protocolVersion = null)
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(host.Client.BaseAddress!, McpEndpoints.Route),
            TransportMode = HttpTransportMode.StreamableHttp,
        });

        return await McpClient.CreateAsync(transport, new McpClientOptions { ProtocolVersion = protocolVersion });
    }

    private static void AssertFluentCmsServer(McpClient client)
    {
        Assert.Equal(McpServiceCollectionExtensions.ServerName, client.ServerInfo.Name);
        Assert.Equal(typeof(McpServiceCollectionExtensions).Assembly.GetName().Version!.ToString(3), client.ServerInfo.Version);
        Assert.NotNull(client.ServerCapabilities.Tools);
    }

    private static async Task<IEnumerable<string>> PendingMigrationsAsync(Dictionary<string, string?> configuration)
    {
        await using var services = TestServices.Build(configuration);
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>().Database.GetPendingMigrationsAsync();
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
