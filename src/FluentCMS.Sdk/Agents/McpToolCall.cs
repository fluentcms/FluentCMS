using System.Text.Json;
using FluentCMS.Content;

namespace FluentCMS.Agents;

/// <summary>A call of a tool.</summary>
/// <param name="Operation">The operation of the call: its principal, site, locale and client.</param>
/// <param name="Name">The name of the tool.</param>
/// <param name="Arguments">The arguments as a JSON object, empty when the client sent none. They are not checked against the input schema.</param>
public sealed record McpToolCall(OperationContext Operation, string Name, JsonElement Arguments);

/// <summary>What a tool answers.</summary>
public sealed record McpToolResult
{
    /// <summary>The answer as text, which is what a model reads. For a failure, the message.</summary>
    public string Text { get; init; } = "";

    /// <summary>The answer as a JSON object that matches <see cref="McpToolDescriptor.OutputSchema"/>, or <see langword="null"/>.</summary>
    public JsonElement? StructuredContent { get; init; }

    /// <summary>Whether the call failed in a way the caller can read and act on.</summary>
    public bool IsError { get; init; }

    /// <summary>The stable code of the failure, such as <c>FCMS3104</c>, or <see langword="null"/>.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>An answer of text.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The result.</returns>
    public static McpToolResult FromText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new McpToolResult { Text = text };
    }

    /// <summary>An answer of structured content, which is also its text.</summary>
    /// <param name="content">A JSON object.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentException">The content is not a JSON object.</exception>
    public static McpToolResult FromJson(JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("The structured content of a tool result is a JSON object.", nameof(content));
        }

        return new McpToolResult { Text = content.GetRawText(), StructuredContent = content };
    }

    /// <summary>A failure the caller can act on, with its code.</summary>
    /// <param name="code">The stable code, the one the REST APIs answer the same failure with, or one the plugin added.</param>
    /// <param name="message">A message for the caller.</param>
    /// <returns>The result.</returns>
    public static McpToolResult Error(string code, string message)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        ArgumentException.ThrowIfNullOrEmpty(message);

        return new McpToolResult { IsError = true, ErrorCode = code, Text = message };
    }
}
