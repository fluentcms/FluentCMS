using FluentCMS.Content;

namespace FluentCMS.Agents;

/// <summary>A prompt as an MCP client sees it in the prompt list.</summary>
public sealed record McpPromptDescriptor
{
    /// <summary>Creates a descriptor.</summary>
    /// <param name="name">The name a client gets the prompt by, unique among the prompts of the server.</param>
    /// <exception cref="ArgumentException">The name is empty.</exception>
    public McpPromptDescriptor(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
    }

    /// <summary>The name.</summary>
    public string Name { get; }

    /// <summary>A name for people, or <see langword="null"/>.</summary>
    public string? Title { get; init; }

    /// <summary>What the prompt is for, or <see langword="null"/>.</summary>
    public string? Description { get; init; }

    /// <summary>The arguments the prompt takes.</summary>
    public IReadOnlyList<McpPromptArgument> Arguments { get; init; } = [];
}

/// <summary>An argument of a prompt.</summary>
/// <param name="Name">The name.</param>
public sealed record McpPromptArgument(string Name)
{
    /// <summary>What the argument is, or <see langword="null"/>.</summary>
    public string? Description { get; init; }

    /// <summary>Whether a client must give the argument.</summary>
    public bool Required { get; init; }
}

/// <summary>A request for a prompt.</summary>
/// <param name="Operation">The operation of the request: its principal, site, locale and client.</param>
/// <param name="Name">The name of the prompt.</param>
/// <param name="Arguments">The arguments by name, empty when the client sent none.</param>
public sealed record McpPromptGet(OperationContext Operation, string Name, IReadOnlyDictionary<string, string> Arguments);

/// <summary>A prompt with its arguments filled in.</summary>
/// <param name="Messages">The messages, in order.</param>
public sealed record McpPromptResult(IReadOnlyList<McpPromptMessage> Messages)
{
    /// <summary>What the prompt is for, or <see langword="null"/>.</summary>
    public string? Description { get; init; }
}

/// <summary>A message of a prompt.</summary>
/// <param name="Role">Who says it.</param>
/// <param name="Text">The text.</param>
public sealed record McpPromptMessage(McpPromptRole Role, string Text);

/// <summary>Who says a message of a prompt.</summary>
public enum McpPromptRole
{
    /// <summary>The user.</summary>
    User = 1,

    /// <summary>The model.</summary>
    Assistant = 2,
}
