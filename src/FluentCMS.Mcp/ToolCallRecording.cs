using System.Text.Json.Nodes;
using FluentCMS.Core.Agents;
using FluentCMS.Core.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FluentCMS.Mcp;

// Every tool call is recorded with the agent that made it and, when the agent declares them in the call's _meta, its model and
// task id (MCP-07). _meta travels with the call, so declaring works the same over HTTP and stdio.
public static class ToolCallRecording
{
    public const string ModelKey = "fluentcms/model";
    public const string TaskKey = "fluentcms/task";

    internal static McpRequestFilter<CallToolRequestParams, CallToolResult> Filter => next => async (context, cancellationToken) =>
    {
        var services = context.Server.Services!;
        var clock = services.GetRequiredService<TimeProvider>();
        var startedAt = clock.GetUtcNow();
        var started = clock.GetTimestamp();
        var outcome = ToolCallOutcome.Errored;

        try
        {
            var result = await next(context, cancellationToken);
            outcome = result.IsError == true ? ToolCallOutcome.Failed : ToolCallOutcome.Succeeded;
            return result;
        }
        finally
        {
            // Both transports refuse a call without an accepted key, so a call that reaches a tool always has an agent.
            if (ClaimsInitiator.From(context.User) is AgentInitiator agent)
            {
                var call = new ToolCall
                {
                    Id = Guid.CreateVersion7(startedAt),
                    Timestamp = startedAt,
                    AgentCredentialId = agent.CredentialId,
                    AgentName = agent.DisplayName,
                    Tool = context.Params?.Name ?? string.Empty,
                    Model = Declared(context.Params?.Meta, ModelKey),
                    TaskId = Declared(context.Params?.Meta, TaskKey),
                    Outcome = outcome,
                    Duration = clock.GetElapsedTime(started),
                };

                await RecordAsync(services, call);
            }
        }
    };

    // A call that already ran is not failed afterwards because its record could not be written; the failure is logged instead.
    private static async Task RecordAsync(IServiceProvider services, ToolCall call)
    {
        try
        {
            await services.GetRequiredService<IToolCallLog>().AppendAsync(call, CancellationToken.None);
        }
        catch (Exception exception)
        {
            services.GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(ToolCallRecording))
                .LogError(exception, "The call of tool {Tool} by agent credential {AgentCredentialId} could not be recorded.", call.Tool, call.AgentCredentialId);
        }
    }

    private static string? Declared(JsonObject? meta, string key)
    {
        return meta?[key] is JsonValue value && value.TryGetValue<string>(out var text) ? ToolCall.Declared(text) : null;
    }
}
