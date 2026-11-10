namespace FluentCMS.Plugins.Mcp;

// Bound from FluentCms:Mcp.
public sealed class McpOptions
{
    public const string Section = "FluentCms:Mcp";

    // What the server calls itself when a client connects: a name for programs and a title for people.
    public string Name { get; set; } = "fluentcms";

    public string Title { get; set; } = "FluentCMS";

    // The content types the tools reach, by name. Empty is every type the caller may read.
    public List<string> ExposedTypes { get; set; } = [];

    // How many of those types get tools of their own, in the order of the schema. The others are reached through the query tool.
    public int MaxExposedTypes { get; set; } = 10;

    // The longest a media_upload waits for the URL it fetches, connection and body together.
    public TimeSpan UploadTimeout { get; set; } = TimeSpan.FromSeconds(30);

    internal bool IsValid() =>
        Name is { Length: > 0 and <= 64 } && Name.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')
        && !string.IsNullOrWhiteSpace(Title) && Title.Length <= 128 && !Title.Any(char.IsControl);

    internal bool ToolsAreValid() =>
        ExposedTypes is not null && ExposedTypes.All(type => !string.IsNullOrWhiteSpace(type))
        && MaxExposedTypes is >= 0 and <= 100
        && UploadTimeout > TimeSpan.Zero && UploadTimeout <= TimeSpan.FromMinutes(10);
}
