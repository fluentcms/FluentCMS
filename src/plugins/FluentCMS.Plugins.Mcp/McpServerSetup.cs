using System.Diagnostics;
using System.Reflection;
using FluentCMS.Content;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FluentCMS.Plugins.Mcp;

// The protocol server. It is stateless: every POST is a server of its own with the services of its request, so any
// instance answers any request, nothing is kept between requests, and there is no GET stream, no session and no legacy SSE
// endpoint. A handler runs inside the operation the request bridge of core opened, which McpRequests reads.
internal static class McpServerSetup
{
    // The informational version of the plugin without its build metadata.
    public static string Version { get; } =
        (typeof(McpServerSetup).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0").Split('+')[0];

    public static void Add(IServiceCollection services)
    {
        services.AddMcpServer().WithHttpTransport(http => http.Stateless = true);
        services.Configure<ModelContextProtocol.AspNetCore.HttpServerTransportOptions>(http => http.ConfigureSessionOptions = McpSiteSetup.InstructAsync);
        services.AddOptions<McpServerOptions>().Configure<IOptions<McpOptions>>((server, options) =>
        {
            server.ServerInfo = new Implementation { Name = options.Value.Name, Title = options.Value.Title, Version = Version };

            var handlers = server.Handlers;
            handlers.ListToolsHandler = static (request, cancellationToken) => Requests(request).ListToolsAsync(cancellationToken);
            handlers.CallToolHandler = static (request, cancellationToken) => Requests(request).CallToolAsync(Params(request), cancellationToken);
            handlers.ListResourcesHandler = static (request, cancellationToken) => Requests(request).ListResourcesAsync(cancellationToken);
            handlers.ListResourceTemplatesHandler = static (request, cancellationToken) => Requests(request).ListResourceTemplatesAsync(cancellationToken);
            handlers.ReadResourceHandler = static (request, cancellationToken) => Requests(request).ReadResourceAsync(Params(request), request.Server.NegotiatedProtocolVersion, cancellationToken);
            handlers.ListPromptsHandler = static (request, cancellationToken) => Requests(request).ListPromptsAsync(cancellationToken);
            handlers.GetPromptHandler = static (request, cancellationToken) => Requests(request).GetPromptAsync(Params(request), cancellationToken);
        });
    }

    private static McpRequests Requests<TParams>(RequestContext<TParams> request) =>
        (request.Services ?? throw new InvalidOperationException("The MCP request has no services of its HTTP request.")).GetRequiredService<McpRequests>();

    private static TParams Params<TParams>(RequestContext<TParams> request) =>
        request.Params ?? throw new McpProtocolException("The request has no params.", McpErrorCode.InvalidParams);
}

internal static class McpTelemetry
{
    // The name of core's one activity source, so that a host that subscribes to it hears the tool calls too.
    private static readonly ActivitySource Source = new("FluentCMS", McpServerSetup.Version);

    // A span for a tool call, with no argument and no result. It is named for the tool once a provider answers for it, so
    // that a name a client made up never becomes a span name.
    public static System.Diagnostics.Activity? StartToolCall(OperationContext operation)
    {
        var activity = Source.StartActivity("mcp tool");
        activity?.SetTag("fluentcms.site", operation.Site.Value.ToString("D"));
        return activity;
    }

    public static void Name(System.Diagnostics.Activity? activity, string tool)
    {
        if (activity is not null)
        {
            activity.DisplayName = $"mcp {tool}";
            activity.SetTag("fluentcms.operation", tool);
        }
    }

    public static void Fail(System.Diagnostics.Activity? activity, string? code)
    {
        activity?.SetStatus(ActivityStatusCode.Error);
        if (code is not null)
        {
            activity?.SetTag("fluentcms.code", code);
        }
    }
}
