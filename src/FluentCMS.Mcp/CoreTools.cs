using System.Text.Encodings.Web;
using System.Text.Json;
using FluentCMS.Core.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FluentCMS.Mcp;

// Puts every tool and resource the Core providers register on the server, built-in and plugin alike (EXT-02). A name used twice
// stops the host at startup rather than letting one tool hide another.
internal sealed class CoreToolsSetup(IEnumerable<IToolProvider> tools, IEnumerable<IResourceProvider> resources) : IConfigureOptions<McpServerOptions>
{
    public void Configure(McpServerOptions options)
    {
        options.ToolCollection ??= [];
        foreach (var tool in tools.SelectMany(provider => provider.Tools))
        {
            if (!options.ToolCollection.TryAdd(new CoreTool(tool)))
            {
                throw new InvalidOperationException($"Two tools are named '{tool.Name}'; every tool needs a name of its own.");
            }
        }

        options.ResourceCollection ??= [];
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var resource in resources.SelectMany(provider => provider.Resources))
        {
            if (!names.Add(resource.Name) || !options.ResourceCollection.TryAdd(new CoreResource(resource)))
            {
                throw new InvalidOperationException($"Two resources are named '{resource.Name}' or share the URI '{resource.UriTemplate}'.");
            }
        }
    }
}

// A failure is a tool error the agent reads and acts on, never a protocol error (UX-03). The JSON goes out as structured
// content and, for clients that read only text, as the text of the result.
internal sealed class CoreTool(AgentTool tool) : McpServerTool
{
    // The text is read by a model, not put in a page, so quotes and apostrophes stay as they are.
    private static readonly JsonSerializerOptions Text = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public override Tool ProtocolTool { get; } = new()
    {
        Name = tool.Name,
        Title = tool.Title,
        Description = tool.Description,
        InputSchema = JsonSerializer.SerializeToElement(tool.InputSchema),
        Annotations = new ToolAnnotations
        {
            Title = tool.Title,
            ReadOnlyHint = tool.ReadOnly,
            DestructiveHint = false,
            OpenWorldHint = false,
        },
    };

    public override IReadOnlyList<object> Metadata => [];

    public override async ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var arguments = JsonSerializer.SerializeToElement(request.Params?.Arguments ?? new Dictionary<string, JsonElement>());
        var result = await tool.InvokeAsync(new ToolCall(arguments, request.Services!), cancellationToken);
        var node = result.ToJson();
        var json = JsonSerializer.SerializeToElement(node);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = node?.ToJsonString(Text) ?? "null" }],
            StructuredContent = json.ValueKind == JsonValueKind.Object ? json : null,
            IsError = !result.IsSuccess,
        };
    }
}

internal sealed class CoreResource(AgentResource resource) : McpServerResource
{
    public override ResourceTemplate ProtocolResourceTemplate { get; } = new()
    {
        Name = resource.Name,
        UriTemplate = resource.UriTemplate,
        Description = resource.Description,
        MimeType = resource.MimeType,
    };

    public override IReadOnlyList<object> Metadata => [];

    public override bool IsMatch(string uri)
    {
        return resource.Match(uri) is not null;
    }

    public override async ValueTask<ReadResourceResult> ReadAsync(RequestContext<ReadResourceRequestParams> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var uri = request.Params.Uri;
        var parameters = resource.Match(uri)
            ?? throw new McpProtocolException($"The resource '{resource.Name}' does not serve '{uri}'.", McpErrorCode.ResourceNotFound);

        var read = await resource.ReadAsync(new ResourceRead(uri, parameters, request.Services!), cancellationToken);
        if (!read.IsSuccess)
        {
            // A resource read has no error result, so an expected failure is a protocol error that still says what to change.
            var notFound = read.Errors.All(error => error.Code.EndsWith("_not_found", StringComparison.Ordinal));
            throw new McpProtocolException(
                string.Join(" ", read.Errors.Select(error => error.Message)),
                notFound ? McpErrorCode.ResourceNotFound : McpErrorCode.InvalidParams);
        }

        return new ReadResourceResult
        {
            Contents = [new TextResourceContents { Uri = uri, MimeType = resource.MimeType, Text = read.Value }],
        };
    }
}
