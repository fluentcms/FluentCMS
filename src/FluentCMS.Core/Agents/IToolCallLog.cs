namespace FluentCMS.Core.Agents;

// Every tool call agents make, appended and never changed (MCP-07). The calls-per-period cap counts these records (SEC-04).
public interface IToolCallLog
{
    // Writes in a transaction of its own, never in a caller's unit of work, so a call whose own changes rolled back is still on record.
    Task AppendAsync(ToolCall call, CancellationToken cancellationToken = default);

    // The outcome of a call recorded as running when it started, in a transaction of its own.
    Task FinishAsync(Guid callId, ToolCallOutcome outcome, TimeSpan duration, CancellationToken cancellationToken = default);
}
