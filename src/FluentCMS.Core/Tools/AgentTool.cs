using System.Text.Json;
using System.Text.Json.Nodes;

namespace FluentCMS.Core.Tools;

// One tool. Its description and the description of every argument are written for an LLM reader: what the tool does, when to
// call it and what to call next (MCP-02). The input schema refuses arguments it does not name, as the arguments reader does.
public sealed class AgentTool
{
    private readonly Func<ToolCall, CancellationToken, Task<ToolResult>> _invoke;

    public AgentTool(
        string name,
        string title,
        string description,
        JsonObject inputSchema,
        bool readOnly,
        Func<ToolCall, CancellationToken, Task<ToolResult>> invoke)
    {
        if (!ToolNames.IsName(name))
        {
            throw new ArgumentException($"'{name}' is not a tool name; use lowercase words joined by underscores, such as 'get_content_type'.", nameof(name));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(inputSchema);
        ArgumentNullException.ThrowIfNull(invoke);

        Name = name;
        Title = title;
        Description = description;
        InputSchema = inputSchema;
        ReadOnly = readOnly;
        _invoke = invoke;
    }

    // Agents and the tool call log know a tool by its name, so it never changes once released.
    public string Name { get; }

    public string Title { get; }

    public string Description { get; }

    public JsonObject InputSchema { get; }

    // Whether the tool only reads. A tool that writes goes through a change set, so a person reviews what it proposes.
    public bool ReadOnly { get; }

    public Task<ToolResult> InvokeAsync(ToolCall call, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(call);
        return _invoke(call, cancellationToken);
    }
}

// The arguments are a JSON object, empty when the agent sent none. The services are the call's own scope, so a tool reads the
// directories as the agent that called it.
public sealed record ToolCall(JsonElement Arguments, IServiceProvider Services);
