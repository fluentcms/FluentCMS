using System.IO.Pipelines;
using FluentCMS.Core.Agents;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Sites;
using FluentCMS.Host;
using FluentCMS.Mcp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FluentCMS.IntegrationTests.Mcp;

// Keys for the MCP tests and the ways an agent connects with one.
internal static class McpAgents
{
    public static readonly UserInitiator Issuer = new(Guid.NewGuid(), "Test administrator");

    // The built-in tools every server lists, sorted by name.
    public static readonly string[] BuiltInTools =
    [
        "create_content_type", "create_entry", "delete_entry", "get_change_set", "get_change_set_diff", "get_content_type", "get_entry",
        "get_entry_history", "get_resolved_entry", "get_schema_document", "get_where_used", "list_content_types", "list_entries",
        "open_change_set", "reopen_change_set", "revise_content_type", "submit_change_set", "update_entry",
    ];

    public static async Task<string[]> ToolNamesAsync(McpClient client)
    {
        return [.. (await client.ListToolsAsync()).Select(tool => tool.Name).Order(StringComparer.Ordinal)];
    }

    // Reports who a call runs as, so a test can see the initiator the services would record.
    public static McpServerTool WhoAmI { get; } = McpServerTool.Create(
        (IServiceProvider services) => services.GetRequiredService<ICurrentInitiator>().Current switch
        {
            AgentInitiator agent => $"agent {agent.CredentialId} {agent.DisplayName}",
            UserInitiator user => $"user {user.UserId} {user.DisplayName}",
            _ => "nobody",
        },
        new McpServerToolCreateOptions { Name = "whoami" });

    public static NewAgentCredential Credential(DateTimeOffset now, string displayName = "pipeline-agent")
    {
        return new NewAgentCredential(
            displayName,
            now.AddDays(30),
            AgentScope.Create([Site.Default.Id], null, null, [AgentOperation.Read]).Value,
            AgentCaps.Create(600, TimeSpan.FromHours(1), 5m, TimeSpan.FromDays(1), 200, 20).Value);
    }

    public static async Task<IssuedAgentCredential> IssueAsync(AdminHost host, Guid administratorId, NewAgentCredential? credential = null)
    {
        var issued = await host.AsUserAsync<IAgentCredentialDirectory, Core.Results.Result<IssuedAgentCredential>>(
            administratorId,
            directory => directory.IssueAsync(credential ?? Credential(DateTimeOffset.UtcNow)));
        return issued.Value;
    }

    // Migrates the database first, so a key can be issued before a stdio process starts on it.
    public static async Task<IssuedAgentCredential> IssueAsync(IEnumerable<KeyValuePair<string, string?>> database)
    {
        await using var services = TestServices.Build(database, collection => collection.AddSingleton<ICurrentInitiator>(new TestInitiator(Issuer)));
        await TestServices.MigrateAsync(services);
        await using var scope = services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IAgentCredentialDirectory>().IssueAsync(Credential(DateTimeOffset.UtcNow))).Value;
    }

    public static async Task RevokeAsync(IEnumerable<KeyValuePair<string, string?>> database, Guid credentialId)
    {
        await using var services = TestServices.Build(database, collection => collection.AddSingleton<ICurrentInitiator>(new TestInitiator(Issuer)));
        await using var scope = services.CreateAsyncScope();
        Assert.True((await scope.ServiceProvider.GetRequiredService<IAgentCredentialDirectory>().RevokeAsync(credentialId)).IsSuccess);
    }

    public static Task<McpClient> ConnectOverHttpAsync(AdminHost host, string key, string? protocolVersion = null)
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(host.Client.BaseAddress!, McpEndpoints.Route),
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {key}" },
        });

        return McpClient.CreateAsync(transport, new McpClientOptions { ProtocolVersion = protocolVersion });
    }

    public static async Task<string> WhoAmIAsync(McpClient client)
    {
        var result = await client.CallToolAsync("whoami");
        return Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
    }
}

// fluentcms mcp in this process, on pipes instead of the console, with the whoami tool added and the key given to it directly.
internal sealed class InProcessStdio : IAsyncDisposable
{
    private readonly Pipe _toServer = new();
    private readonly Pipe _fromServer = new();
    private readonly IHost _host;

    private InProcessStdio(IReadOnlyDictionary<string, string?> settings, string? agentKey)
    {
        _host = FluentCmsHost.BuildMcpHost(
            [],
            _toServer.Reader.AsStream(),
            _fromServer.Writer.AsStream(),
            builder =>
            {
                builder.Configuration.AddInMemoryCollection(settings);
                builder.Services.AddSingleton(McpAgents.WhoAmI);
            },
            agentKey);
    }

    public static async Task<InProcessStdio> StartAsync(IReadOnlyDictionary<string, string?> settings, string? agentKey)
    {
        var stdio = new InProcessStdio(settings, agentKey);
        await stdio._host.StartAsync();
        return stdio;
    }

    public Task<McpClient> ConnectAsync()
    {
        return McpClient.CreateAsync(new StreamClientTransport(_toServer.Writer.AsStream(), _fromServer.Reader.AsStream()));
    }

    public async ValueTask DisposeAsync()
    {
        await _toServer.Writer.CompleteAsync();
        await _host.StopAsync();
        _host.Dispose();
    }
}
