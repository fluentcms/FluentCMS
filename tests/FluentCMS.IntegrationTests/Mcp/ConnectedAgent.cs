using System.Text.Json.Nodes;
using FluentCMS.Core.Agents;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace FluentCMS.IntegrationTests.Mcp;

// An agent connected over HTTP to a running host with its own database, and the administrator who issued its credential.
internal sealed class ConnectedAgent(
    TemporaryInstall install,
    TemporaryDatabase database,
    AdminHost host,
    Guid administrator,
    AgentCredential credential,
    McpClient client) : IAsyncDisposable
{
    public AgentCredential Credential { get; } = credential;

    // Every type unless types narrows the scope; reads, creates and updates unless operations says otherwise.
    public static async Task<ConnectedAgent> StartAsync(
        string provider,
        int itemsPerChangeSet = 200,
        IReadOnlyList<string>? types = null,
        AgentOperation[]? operations = null,
        IDictionary<string, string?>? settings = null,
        Action<WebApplicationBuilder>? configure = null)
    {
        var install = new TemporaryInstall();
        var database = await TemporaryDatabase.CreateAsync(provider);
        var host = await AdminHost.StartAsync(
            install,
            settings: new Dictionary<string, string?>(database.Configuration.Concat(settings ?? new Dictionary<string, string?>())),
            configure: configure);
        var administrator = await host.CreateFirstAdministratorAsync();
        var issued = await McpAgents.IssueAsync(host, administrator, new NewAgentCredential(
            "schema-agent",
            DateTimeOffset.UtcNow.AddDays(30),
            AgentScope.Create([Site.Default.Id], types, null, operations ?? [AgentOperation.Read, AgentOperation.Create, AgentOperation.Update]).Value,
            AgentCaps.Create(600, TimeSpan.FromHours(1), 5m, TimeSpan.FromDays(1), itemsPerChangeSet, 20).Value));
        var client = await McpAgents.ConnectOverHttpAsync(host, issued.Key);
        return new ConnectedAgent(install, database, host, administrator, issued.Credential, client);
    }

    public async Task<JsonObject> CallAsync(string tool, Dictionary<string, object?> arguments)
    {
        var result = await client.CallToolAsync(tool, arguments);
        Assert.False(result.IsError ?? false, Text(result));
        return JsonNode.Parse(Text(result))!.AsObject();
    }

    public async Task<JsonObject> FailAsync(string tool, Dictionary<string, object?> arguments)
    {
        var result = await client.CallToolAsync(tool, arguments);
        Assert.True(result.IsError, Text(result));
        return JsonNode.Parse(Text(result))!.AsObject();
    }

    public async Task<T> AsPersonAsync<TService, T>(Func<TService, Task<Core.Results.Result<T>>> action)
        where TService : notnull
    {
        return (await host.AsUserAsync<TService, Core.Results.Result<T>>(administrator, action)).Value;
    }

    public Task<Core.Results.Result> AsPersonAsync<TService>(Func<TService, Task<Core.Results.Result>> action)
        where TService : notnull
    {
        return host.AsUserAsync<TService, Core.Results.Result>(administrator, action);
    }

    // Runs as the agent, as a call over MCP would, for work a test drives without a tool.
    public async Task<T> AsAgentAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        var accessor = host.App.Services.GetRequiredService<IHttpContextAccessor>();
        await using var scope = host.App.Services.CreateAsyncScope();
        accessor.HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider, User = AgentPrincipal.For(Credential, "test") };
        try
        {
            return await action(scope.ServiceProvider);
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    public Task<ContentType> CreateAsPersonAsync(string slug)
    {
        return AsPersonAsync<IContentTypeDirectory, ContentType>(directory =>
            directory.CreateAsync(new NewContentType(slug, slug, new ContentTypeDefinition([new FieldDefinition("title", "text")])), TestChangeSetTargets.ContentModel));
    }

    public async Task<T> QueryAsync<T>(Func<FluentCmsDbContext, Task<T>> query)
    {
        await using var scope = host.App.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>());
    }

    public async ValueTask DisposeAsync()
    {
        await client.DisposeAsync();
        await host.DisposeAsync();
        await database.DisposeAsync();
        install.Dispose();
    }

    private static string Text(CallToolResult result)
    {
        return Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
    }
}
