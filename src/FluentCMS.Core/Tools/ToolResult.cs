using System.Text.Json.Nodes;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Tools;

// What a tool returns: a JSON value, or the errors that say what to change and, where it helps, the call that finds a valid
// value (UX-03). An expected failure is a result, never an exception, so the agent reads it and tries again.
public sealed class ToolResult
{
    private ToolResult(JsonNode? value, IReadOnlyList<Error> errors, string? hint)
    {
        Value = value;
        Errors = errors;
        Hint = hint;
    }

    public bool IsSuccess => Errors.Count == 0;

    public JsonNode? Value { get; }

    public IReadOnlyList<Error> Errors { get; }

    public string? Hint { get; }

    public static ToolResult Success(JsonNode? value)
    {
        return new ToolResult(value, [], null);
    }

    public static ToolResult Failure(IEnumerable<Error> errors, string? hint = null)
    {
        ArgumentNullException.ThrowIfNull(errors);

        var list = errors.ToList();
        if (list.Count == 0)
        {
            throw new ArgumentException("A failed tool result names at least one error.", nameof(errors));
        }

        return new ToolResult(null, list, hint);
    }

    public static ToolResult From<T>(Result<T> result, Func<T, JsonNode?> project, string? hint = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(project);

        return result.IsSuccess ? Success(project(result.Value)) : Failure(result.Errors, hint);
    }

    // The JSON an agent reads either way: the value itself, or {"errors": [{"code", "message"}], "hint"}.
    public JsonNode? ToJson()
    {
        if (IsSuccess)
        {
            return Value?.DeepClone();
        }

        var errors = new JsonArray();
        foreach (var error in Errors)
        {
            errors.Add(new JsonObject { ["code"] = error.Code, ["message"] = error.Message });
        }

        var failure = new JsonObject { ["errors"] = errors };
        if (Hint is not null)
        {
            failure["hint"] = Hint;
        }

        return failure;
    }
}
