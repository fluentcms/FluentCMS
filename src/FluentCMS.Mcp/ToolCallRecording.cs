using System.Text.Json.Nodes;
using FluentCMS.Core.Agents;
using FluentCMS.Core.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FluentCMS.Mcp;

// Every tool call is recorded with the agent that made it and, when the agent declares them in the call's _meta, its model and
// task id (MCP-07). _meta travels with the call, so declaring works the same over HTTP and stdio. A call is recorded as running before
// its tool runs, in the transaction that counts it toward the credential's calls per period, so concurrent calls cannot all pass the cap;
// a call past it is recorded as refused and its tool never runs (SEC-04).
public static class ToolCallRecording
{
    public const string ModelKey = "fluentcms/model";
    public const string TaskKey = "fluentcms/task";

    internal static McpRequestFilter<CallToolRequestParams, CallToolResult> Filter => next => async (context, cancellationToken) =>
    {
        // Both transports refuse a call without an accepted key, so a call that reaches a tool always has an agent.
        if (ClaimsInitiator.From(context.User) is not AgentInitiator agent)
        {
            return await next(context, cancellationToken);
        }

        var services = context.Server.Services!;
        var clock = services.GetRequiredService<TimeProvider>();
        var startedAt = clock.GetUtcNow();
        var started = clock.GetTimestamp();
        var call = new ToolCall
        {
            Id = Guid.CreateVersion7(startedAt),
            Timestamp = startedAt,
            AgentCredentialId = agent.CredentialId,
            AgentName = agent.DisplayName,
            Tool = context.Params?.Name ?? string.Empty,
            Model = Declared(context.Params?.Meta, ModelKey),
            TaskId = Declared(context.Params?.Meta, TaskKey),
            Outcome = ToolCallOutcome.Running,
            Duration = TimeSpan.Zero,
        };

        CallToolResult? refusal;
        try
        {
            refusal = await ClaimAsync(services, call, cancellationToken);
        }
        catch (Exception)
        {
            // The claim rolled back with the call it recorded, so the call is logged again on its own, and its tool never runs.
            await RecordAsync(services, call, log => log.AppendAsync(
                call with { Outcome = ToolCallOutcome.Errored, Duration = clock.GetElapsedTime(started) },
                CancellationToken.None));
            throw;
        }

        if (refusal is not null)
        {
            return refusal;
        }

        var outcome = ToolCallOutcome.Errored;
        try
        {
            var result = await next(context, cancellationToken);
            outcome = result.IsError == true ? ToolCallOutcome.Failed : ToolCallOutcome.Succeeded;
            return result;
        }
        finally
        {
            var duration = clock.GetElapsedTime(started);
            await RecordAsync(services, call, log => log.FinishAsync(call.Id, outcome, duration, CancellationToken.None));
        }
    };

    // Within its calls per period the call is recorded as running and the tool runs; past it, or under a credential refused since its
    // key was accepted, it is recorded as refused and the agent is told why in a tool result it can read (SEC-04, UX-03). The claim
    // gets a service scope of its own; the caller is still the agent.
    private static async Task<CallToolResult?> ClaimAsync(IServiceProvider services, ToolCall call, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var allowed = await scope.ServiceProvider.GetRequiredService<IAgentPermissions>().ClaimToolCallAsync(call, cancellationToken);

        return allowed.IsSuccess
            ? null
            : new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = string.Join(" ", allowed.Errors.Select(error => error.Message)) }],
            };
    }

    // A call is not failed again because its record could not be written; that failure is logged instead.
    private static async Task RecordAsync(IServiceProvider services, ToolCall call, Func<IToolCallLog, Task> write)
    {
        try
        {
            await write(services.GetRequiredService<IToolCallLog>());
        }
        catch (Exception exception)
        {
            services.GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(ToolCallRecording))
                .LogError(exception, "The outcome of tool {Tool} for agent credential {AgentCredentialId} could not be recorded.", call.Tool, call.AgentCredentialId);
        }
    }

    private static string? Declared(JsonObject? meta, string key)
    {
        return meta?[key] is JsonValue value && value.TryGetValue<string>(out var text) ? ToolCall.Declared(text) : null;
    }
}
