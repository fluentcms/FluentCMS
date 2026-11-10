using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Agents;
using FluentCMS.Content;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace FluentCMS.Plugins.Mcp;

// Answers the protocol's requests from what the providers of the plugins offer. It carries the identity and the intent
// and decides nothing: a provider reaches content through the services, which enforce what the caller may do, and what
// a service refuses is answered with the code the REST APIs give it. The providers of a plugin that is disabled for the
// site of the request are not asked.
internal sealed class McpRequests(
    IOperationContextAccessor operations,
    IMcpProviders providers,
    IPluginState plugins,
    IProblemDescriber problems,
    McpInstructions instructions,
    IServiceProvider services,
    TimeProvider time,
    ILogger<McpRequests> logger)
{
    // The key of a tool result's _meta that holds the FCMS code of a failure.
    public const string CodeMeta = "fluentcms/code";

    private const string InvalidParamsForMissingResource = "2026-07-28";

    public async ValueTask<ListToolsResult> ListToolsAsync(CancellationToken cancellationToken)
    {
        var result = new ListToolsResult();
        HashSet<string> names = new(StringComparer.Ordinal);
        var instructed = await instructions.ByTypeAsync(Operation().Site, cancellationToken);
        await ListAsync<IMcpToolProvider, McpToolDescriptor>(
            providers.Tools,
            (provider, request) => provider.ListToolsAsync(request, cancellationToken),
            (tool, source) =>
            {
                if (!names.Add(tool.Name))
                {
                    McpLog.Skipped(logger, source.Provider.Name, source.Plugin, tool.Name, "an earlier provider lists a tool of that name.");
                    return;
                }

                try
                {
                    result.Tools.Add(ToolOf(Instructed(tool, instructed)));
                }
                catch (ArgumentException exception)
                {
                    names.Remove(tool.Name);
                    McpLog.Skipped(logger, source.Provider.Name, source.Plugin, tool.Name, exception.Message);
                }
            },
            cancellationToken);
        return result;
    }

    public async ValueTask<CallToolResult> CallToolAsync(CallToolRequestParams parameters, CancellationToken cancellationToken)
    {
        var operation = Operation();
        var call = new McpToolCall(operation, parameters.Name, JsonSerializer.SerializeToElement(parameters.Arguments ?? new Dictionary<string, JsonElement>()));
        var started = time.GetTimestamp();
        using var span = McpTelemetry.StartToolCall(operation);

        McpToolResult? result = null;
        try
        {
            foreach (var provider in await EnabledAsync<IMcpToolProvider>(providers.Tools, operation, cancellationToken))
            {
                result = await provider.CallToolAsync(call, cancellationToken);
                if (result is not null)
                {
                    break;
                }
            }
        }
        catch (Exception exception) when (!Cancelled(exception, cancellationToken))
        {
            result = Refusal(exception, "tools/call", parameters.Name);
        }

        if (result is null)
        {
            McpTelemetry.Fail(span, McpErrorCodes.UnknownTool);
            throw new McpProtocolException($"{McpErrorCodes.UnknownTool}: No tool named '{parameters.Name}' is available to the caller.", McpErrorCode.InvalidParams);
        }

        McpTelemetry.Name(span, parameters.Name);
        if (result.IsError)
        {
            McpTelemetry.Fail(span, result.ErrorCode);
        }

        var elapsed = time.GetElapsedTime(started);
        McpLog.ToolCalled(
            logger,
            parameters.Name,
            operation.Principal.Kind,
            operation.Principal.Id,
            operation.ClientId,
            operation.Site.Value,
            result.IsError ? "failed" : "succeeded",
            result.ErrorCode,
            elapsed.TotalMilliseconds);
        return ResultOf(result);
    }

    public async ValueTask<ListResourcesResult> ListResourcesAsync(CancellationToken cancellationToken)
    {
        var result = new ListResourcesResult();
        foreach (var resource in await ResourcesAsync(cancellationToken))
        {
            if (!resource.IsTemplate)
            {
                result.Resources.Add(new Resource { Uri = resource.Uri, Name = resource.Name, Title = resource.Title, Description = resource.Description, MimeType = resource.MimeType });
            }
        }

        return result;
    }

    public async ValueTask<ListResourceTemplatesResult> ListResourceTemplatesAsync(CancellationToken cancellationToken)
    {
        var result = new ListResourceTemplatesResult();
        foreach (var resource in await ResourcesAsync(cancellationToken))
        {
            if (resource.IsTemplate)
            {
                result.ResourceTemplates.Add(new ResourceTemplate { UriTemplate = resource.Uri, Name = resource.Name, Title = resource.Title, Description = resource.Description, MimeType = resource.MimeType });
            }
        }

        return result;
    }

    public async ValueTask<ReadResourceResult> ReadResourceAsync(ReadResourceRequestParams parameters, string? protocolVersion, CancellationToken cancellationToken)
    {
        var operation = Operation();
        var read = new McpResourceRead(operation, parameters.Uri);
        IReadOnlyList<McpResourceContent>? contents = null;
        try
        {
            foreach (var provider in await EnabledAsync<IMcpResourceProvider>(providers.Resources, operation, cancellationToken))
            {
                contents = await provider.ReadResourceAsync(read, cancellationToken);
                if (contents is not null)
                {
                    break;
                }
            }
        }
        catch (Exception exception) when (!Cancelled(exception, cancellationToken))
        {
            throw Refused(exception, "resources/read", parameters.Uri);
        }

        if (contents is null)
        {
            // The revision of the protocol settles which error a missing resource is: invalid params from 2026-07-28 on,
            // and the resource-not-found code before it. A revision is a date, so the text compares as the dates do.
            throw new McpProtocolException(
                $"{McpErrorCodes.UnknownResource}: No resource '{parameters.Uri}' is available to the caller.",
                string.CompareOrdinal(protocolVersion, InvalidParamsForMissingResource) >= 0 ? McpErrorCode.InvalidParams : McpErrorCode.ResourceNotFound);
        }

        return new ReadResourceResult
        {
            Contents =
            [
                .. contents.Select(content => content.Text is { } text
                    ? (ResourceContents)new TextResourceContents { Uri = content.Uri, MimeType = content.MimeType, Text = text }
                    : BlobResourceContents.FromBytes(content.Data, content.Uri, content.MimeType)),
            ],
        };
    }

    public async ValueTask<ListPromptsResult> ListPromptsAsync(CancellationToken cancellationToken)
    {
        var result = new ListPromptsResult();
        HashSet<string> names = new(StringComparer.Ordinal);
        await ListAsync<IMcpPromptProvider, McpPromptDescriptor>(
            providers.Prompts,
            (provider, request) => provider.ListPromptsAsync(request, cancellationToken),
            (prompt, source) =>
            {
                if (!names.Add(prompt.Name))
                {
                    McpLog.Skipped(logger, source.Provider.Name, source.Plugin, prompt.Name, "an earlier provider lists a prompt of that name.");
                    return;
                }

                result.Prompts.Add(new Prompt
                {
                    Name = prompt.Name,
                    Title = prompt.Title,
                    Description = prompt.Description,
                    Arguments = [.. prompt.Arguments.Select(argument => new PromptArgument { Name = argument.Name, Description = argument.Description, Required = argument.Required })],
                });
            },
            cancellationToken);
        return result;
    }

    public async ValueTask<GetPromptResult> GetPromptAsync(GetPromptRequestParams parameters, CancellationToken cancellationToken)
    {
        var operation = Operation();
        var arguments = (parameters.Arguments ?? new Dictionary<string, JsonElement>())
            .ToDictionary(argument => argument.Key, argument => argument.Value.ValueKind == JsonValueKind.String ? argument.Value.GetString()! : argument.Value.GetRawText(), StringComparer.Ordinal);
        var get = new McpPromptGet(operation, parameters.Name, arguments);
        McpPromptResult? prompt = null;
        try
        {
            foreach (var provider in await EnabledAsync<IMcpPromptProvider>(providers.Prompts, operation, cancellationToken))
            {
                prompt = await provider.GetPromptAsync(get, cancellationToken);
                if (prompt is not null)
                {
                    break;
                }
            }
        }
        catch (Exception exception) when (!Cancelled(exception, cancellationToken))
        {
            throw Refused(exception, "prompts/get", parameters.Name);
        }

        if (prompt is null)
        {
            throw new McpProtocolException($"{McpErrorCodes.UnknownPrompt}: No prompt named '{parameters.Name}' is available to the caller.", McpErrorCode.InvalidParams);
        }

        return new GetPromptResult
        {
            Description = prompt.Description,
            Messages =
            [
                .. prompt.Messages.Select(message => new PromptMessage
                {
                    Role = message.Role == McpPromptRole.Assistant ? Role.Assistant : Role.User,
                    Content = new TextContentBlock { Text = message.Text },
                }),
            ],
        };
    }

    private static bool Cancelled(Exception exception, CancellationToken cancellationToken) =>
        exception is OperationCanceledException && cancellationToken.IsCancellationRequested;

    // The instructions of a site for a content type follow the description of each tool of that type. Only a tool a
    // provider listed for the caller gets them, so they reach no one the type's tools do not.
    private static McpToolDescriptor Instructed(McpToolDescriptor tool, IReadOnlyDictionary<string, string> instructed) =>
        tool.ContentType is { } type && instructed.TryGetValue(type, out var text)
            ? tool with { Description = $"{tool.Description}\n\nInstructions of this site for {type}:\n{text}" }
            : tool;

    // The hints are always written, since a client reads a missing one as the protocol's default, which is destructive
    // and open to the world.
    private static Tool ToolOf(McpToolDescriptor tool) => new()
    {
        Name = tool.Name,
        Title = tool.Title,
        Description = tool.Description,
        InputSchema = tool.InputSchema,
        OutputSchema = tool.OutputSchema,
        Annotations = new ToolAnnotations
        {
            Title = tool.Title,
            ReadOnlyHint = tool.Annotations.ReadOnly,
            DestructiveHint = !tool.Annotations.ReadOnly && tool.Annotations.Destructive,
            IdempotentHint = tool.Annotations.Idempotent,
            OpenWorldHint = tool.Annotations.OpenWorld,
        },
    };

    // A failure names its code first in the text, which is what a model reads, and again under _meta for a program.
    private static CallToolResult ResultOf(McpToolResult result) => new()
    {
        Content = [new TextContentBlock { Text = result is { IsError: true, ErrorCode: { } code } ? $"{code}: {result.Text}" : result.Text }],
        StructuredContent = result.StructuredContent,
        IsError = result.IsError,
        Meta = result is { IsError: true, ErrorCode: { } named } ? new JsonObject { [CodeMeta] = named } : null,
    };

    // The operation the request bridge of core opened for the request, which every provider call runs in.
    private OperationContext Operation() =>
        operations.Current ?? throw new InvalidOperationException("The MCP request runs outside an operation; the MCP endpoint is mapped through the FluentCMS request bridge.");

    private async ValueTask<List<TProvider>> EnabledAsync<TProvider>(IReadOnlyList<McpProviderRegistration> registrations, OperationContext operation, CancellationToken cancellationToken)
        where TProvider : class
    {
        List<TProvider> enabled = [];
        foreach (var registration in registrations)
        {
            if (await plugins.IsEnabledAsync(registration.Plugin, operation.Site, cancellationToken))
            {
                enabled.Add((TProvider)services.GetRequiredService(registration.Provider));
            }
        }

        return enabled;
    }

    // A provider that fails to list is left out and logged, so that one broken plugin does not empty the list of the others.
    private async ValueTask ListAsync<TProvider, TItem>(
        IReadOnlyList<McpProviderRegistration> registrations,
        Func<TProvider, McpListRequest, ValueTask<IReadOnlyList<TItem>>> list,
        Action<TItem, McpProviderRegistration> add,
        CancellationToken cancellationToken)
        where TProvider : class
    {
        var operation = Operation();
        var request = new McpListRequest(operation);
        foreach (var registration in registrations)
        {
            if (!await plugins.IsEnabledAsync(registration.Plugin, operation.Site, cancellationToken))
            {
                continue;
            }

            IReadOnlyList<TItem> items;
            try
            {
                items = await list((TProvider)services.GetRequiredService(registration.Provider), request);
            }
            catch (Exception exception) when (!Cancelled(exception, cancellationToken))
            {
                McpLog.ListFailed(logger, exception, registration.Provider.Name, registration.Plugin);
                continue;
            }

            foreach (var item in items)
            {
                add(item, registration);
            }
        }
    }

    private async ValueTask<List<McpResourceDescriptor>> ResourcesAsync(CancellationToken cancellationToken)
    {
        List<McpResourceDescriptor> resources = [];
        HashSet<string> uris = new(StringComparer.Ordinal);
        await ListAsync<IMcpResourceProvider, McpResourceDescriptor>(
            providers.Resources,
            (provider, request) => provider.ListResourcesAsync(request, cancellationToken),
            (resource, source) =>
            {
                if (uris.Add(resource.Uri))
                {
                    resources.Add(resource);
                }
                else
                {
                    McpLog.Skipped(logger, source.Provider.Name, source.Plugin, resource.Uri, "an earlier provider lists a resource of that URI.");
                }
            },
            cancellationToken);
        return resources;
    }

    // What a tool answers when it, or a service it called, threw: the problem the REST APIs would answer, with each
    // validation error on a line of its own.
    private McpToolResult Refusal(Exception exception, string request, string name)
    {
        var problem = Describe(exception, request, name);
        var text = new StringBuilder(problem.Detail);
        foreach (var error in problem.Errors)
        {
            text.Append('\n').Append("- ").Append(error.Path).Append(": ").Append(error.Message).Append(" (").Append(error.Code).Append(')');
        }

        return McpToolResult.Error(problem.Code, text.ToString());
    }

    private McpProtocolException Refused(Exception exception, string request, string name)
    {
        var problem = Describe(exception, request, name);
        return new McpProtocolException($"{problem.Code}: {problem.Detail}", problem.Status >= 500 ? McpErrorCode.InternalError : McpErrorCode.InvalidParams);
    }

    private ProblemDescription Describe(Exception exception, string request, string name)
    {
        var problem = problems.Describe(exception);
        if (problem.Status >= 500)
        {
            McpLog.RequestFailed(logger, exception, request, name, problem.Code);
        }

        return problem;
    }
}
