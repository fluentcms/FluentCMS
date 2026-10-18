using FluentCMS.Core.Agents;

namespace FluentCMS.Infrastructure.Agents;

// The flat shape of a ToolCall in the ToolCalls table; rows are inserted and never changed.
public sealed class ToolCallRow
{
    public Guid Id { get; set; }

    public DateTimeOffset Timestamp { get; set; }

    public Guid AgentCredentialId { get; set; }

    public string AgentName { get; set; } = string.Empty;

    public string Tool { get; set; } = string.Empty;

    public string? Model { get; set; }

    public string? TaskId { get; set; }

    public ToolCallOutcome Outcome { get; set; }

    public TimeSpan Duration { get; set; }

    internal static ToolCallRow From(ToolCall call)
    {
        return new ToolCallRow
        {
            Id = call.Id,
            Timestamp = call.Timestamp,
            AgentCredentialId = call.AgentCredentialId,
            AgentName = call.AgentName,
            Tool = call.Tool,
            Model = call.Model,
            TaskId = call.TaskId,
            Outcome = call.Outcome,
            Duration = call.Duration,
        };
    }
}
