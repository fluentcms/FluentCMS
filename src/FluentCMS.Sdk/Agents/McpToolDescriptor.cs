using System.Text.Json;
using System.Text.RegularExpressions;

namespace FluentCMS.Agents;

/// <summary>A tool as an MCP client sees it in the tool list.</summary>
public sealed partial record McpToolDescriptor
{
    /// <summary>Creates a descriptor.</summary>
    /// <param name="name">The name; see <see cref="IsValidName"/>.</param>
    /// <param name="description">What the tool does and when to use it, written for the model that chooses it.</param>
    /// <param name="inputSchema">The JSON Schema of the arguments, an object schema.</param>
    /// <exception cref="ArgumentException">The name is not valid, the description is empty, or the schema is not a JSON object.</exception>
    public McpToolDescriptor(string name, string description, JsonElement inputSchema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        if (!IsValidName(name))
        {
            throw new ArgumentException($"'{name}' is not a tool name: use 1 to 128 letters, digits, underscores, hyphens and dots, such as 'article_list'.", nameof(name));
        }

        if (inputSchema.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("The input schema of a tool is a JSON object, such as { \"type\": \"object\" }.", nameof(inputSchema));
        }

        Name = name;
        Description = description;
        InputSchema = inputSchema;
    }

    /// <summary>The name a client calls the tool by, unique among the tools of the server.</summary>
    public string Name { get; }

    /// <summary>What the tool does and when to use it. The server may add the instructions of the site to it.</summary>
    public string Description { get; init; }

    /// <summary>The JSON Schema of the arguments.</summary>
    public JsonElement InputSchema { get; }

    /// <summary>A name for people, or <see langword="null"/>.</summary>
    public string? Title { get; init; }

    /// <summary>
    /// The name of the content type the tool belongs to, such as <c>article</c> for <c>article_list</c>, or
    /// <see langword="null"/> for a tool that is not of one type. The server adds the instructions of that type to the description.
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>The JSON Schema of <see cref="McpToolResult.StructuredContent"/>, or <see langword="null"/> when the tool has none.</summary>
    public JsonElement? OutputSchema { get; init; }

    /// <summary>What the tool may do, which a client shows before it lets the tool run.</summary>
    public McpToolAnnotations Annotations { get; init; } = new();

    /// <summary>Checks a tool name.</summary>
    /// <param name="name">The name.</param>
    /// <returns><see langword="true"/> when the name is 1 to 128 ASCII letters, digits, underscores, hyphens and dots.</returns>
    public static bool IsValidName(string? name) => name is not null && NamePattern().IsMatch(name);

    [GeneratedRegex(@"^[A-Za-z0-9_.-]{1,128}\z", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}

/// <summary>
/// What a tool may do. They are hints for the client and never a control: the services enforce what the caller may do. A
/// tool that says nothing is taken as one that changes data and may destroy it.
/// </summary>
public sealed record McpToolAnnotations
{
    /// <summary>The tool changes nothing.</summary>
    public bool ReadOnly { get; init; }

    /// <summary>The tool may delete or overwrite what is there, rather than only add to it. It says nothing for a read-only tool.</summary>
    public bool Destructive { get; init; } = true;

    /// <summary>Calling the tool again with the same arguments changes nothing more.</summary>
    public bool Idempotent { get; init; }

    /// <summary>The tool reaches outside the CMS, such as a URL it fetches.</summary>
    public bool OpenWorld { get; init; }
}
