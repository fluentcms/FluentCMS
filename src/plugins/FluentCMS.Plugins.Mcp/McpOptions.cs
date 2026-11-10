namespace FluentCMS.Plugins.Mcp;

// Bound from FluentCms:Mcp.
public sealed class McpOptions
{
    public const string Section = "FluentCms:Mcp";

    // What the server calls itself when a client connects: a name for programs and a title for people.
    public string Name { get; set; } = "fluentcms";

    public string Title { get; set; } = "FluentCMS";

    internal bool IsValid() =>
        Name is { Length: > 0 and <= 64 } && Name.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')
        && !string.IsNullOrWhiteSpace(Title) && Title.Length <= 128 && !Title.Any(char.IsControl);
}
