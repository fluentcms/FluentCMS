namespace FluentCMS.Core.Agents;

public enum ToolCallOutcome
{
    // The tool ran and returned a result.
    Succeeded,

    // The tool ran and returned an error result, such as a refused change.
    Failed,

    // The tool threw instead of returning a result.
    Errored,

    // The tool never ran because the call was over the credential's calls per period; not counted toward that cap.
    Refused,

    // The tool is running; the outcome replaces this when it ends. It counts toward the cap, so calls made at once take their places in turn.
    Running,
}

// One tool call an agent made (MCP-07). Arguments and results are not kept: they hold content, and the audit log records every
// change a tool makes. The agent is named by its credential, never by its key (SEC-06).
public sealed record ToolCall
{
    // Agents declare a model and a task id themselves, so what they send is kept to this length.
    public const int MaxDeclaredLength = 128;

    public required Guid Id { get; init; }

    // When the call started.
    public required DateTimeOffset Timestamp { get; init; }

    public required Guid AgentCredentialId { get; init; }

    public required string AgentName { get; init; }

    public required string Tool { get; init; }

    public string? Model { get; init; }

    public string? TaskId { get; init; }

    public required ToolCallOutcome Outcome { get; init; }

    public required TimeSpan Duration { get; init; }

    // A declared value as it is kept: blank is no value, and a long one is cut to the maximum length.
    public static string? Declared(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= MaxDeclaredLength ? trimmed : trimmed[..MaxDeclaredLength];
    }
}
