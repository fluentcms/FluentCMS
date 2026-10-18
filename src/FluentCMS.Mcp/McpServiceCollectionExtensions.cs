using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;

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
    public static IServiceCollection AddFluentCmsMcpStdio(this IServiceCollection services, Stream? input = null, Stream? output = null)
    {
        var server = AddServer(services);

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
        return services.AddMcpServer(options =>
        {
            // Without this the SDK names the server after the entry assembly.
            options.ServerInfo = new Implementation
            {
                Name = ServerName,
                Version = typeof(McpServiceCollectionExtensions).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            };

            // Tools arrive once agents authenticate; until then clients find an empty list rather than no tools capability.
            options.Capabilities = new ServerCapabilities { Tools = new ToolsCapability() };
        });
    }
}
