using FluentCMS.Core.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FluentCMS.Mcp;

public static class McpServiceCollectionExtensions
{
    public const string ServerName = "FluentCMS";

    // Stateless, so any instance answers any request and the server never calls the client back (ADR-0007).
    public static IServiceCollection AddFluentCmsMcpHttp(this IServiceCollection services)
    {
        AddServer(services).WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless);
        return services;
    }

    // One session that ends when its input closes, and stops the host with it. Streams other than the console run it in-process.
    // Every message is answered only for the agent whose key the process was given, and runs as that agent.
    public static IServiceCollection AddFluentCmsMcpStdio(this IServiceCollection services, string? agentKey, Stream? input = null, Stream? output = null)
    {
        var server = AddServer(services).WithMessageFilters(filters => filters.AddIncomingFilter(StdioAgentAuthentication.Filter(agentKey)));
        services.AddScoped<ICurrentInitiator, McpMessageInitiator>();

        if (input is null && output is null)
        {
            server.WithStdioServerTransport();
        }
        else
        {
            server.WithStreamServerTransport(input ?? Stream.Null, output ?? Stream.Null);
        }

        return services;
    }

    private static IMcpServerBuilder AddServer(IServiceCollection services)
    {
        // Built when the host starts, so two tools with one name stop it there instead of at the first call.
        services.AddSingleton<IConfigureOptions<McpServerOptions>, CoreToolsSetup>();
        services.AddOptions<McpServerOptions>().ValidateOnStart();

        return services.AddMcpServer(options =>
        {
            // Without this the SDK names the server after the entry assembly.
            options.ServerInfo = new Implementation
            {
                Name = ServerName,
                Version = typeof(McpServiceCollectionExtensions).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            };
        }).WithRequestFilters(filters => filters.AddCallToolFilter(ToolCallRecording.Filter));
    }
}
