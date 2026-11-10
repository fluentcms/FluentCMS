namespace FluentCMS.Plugins.Mcp;

// Bound from FluentCms:Mcp.
public sealed class McpOptions
{
    public const string Section = "FluentCms:Mcp";

    internal const int MinBytes = 4096;

    internal const int MinListTextLength = 16;

    // What the server calls itself when a client connects: a name for programs and a title for people.
    public string Name { get; set; } = "fluentcms";

    public string Title { get; set; } = "FluentCMS";

    // The content types the tools reach, by name. Empty is every type the caller may read.
    public List<string> ExposedTypes { get; set; } = [];

    // How many of those types get tools of their own, in the order of the schema. The others are reached through the query tool.
    public int MaxExposedTypes { get; set; } = 10;

    // The longest a media_upload waits for the URL it fetches, connection and body together.
    public TimeSpan UploadTimeout { get; set; } = TimeSpan.FromSeconds(30);

    // The most entries one call of the bulk tool names or selects.
    public int MaxBulkItems { get; set; } = 100;

    // The largest body of a request to /mcp, in bytes. Core's cap applies as well, so the lower of the two holds.
    public long MaxRequestBytes { get; set; } = 1024 * 1024;

    // The largest list a content tool answers, in bytes of its JSON. A longer one ends at an entry, with the cursor of the rest.
    public int MaxResultBytes { get; set; } = 256 * 1024;

    // The characters of a text that a list shows before it shortens it. {type}_get reads it whole.
    public int MaxListTextLength { get; set; } = 500;

    // The tool calls a client may make in a minute.
    public McpCallLimitOptions Limits { get; set; } = new();

    internal bool IsValid() =>
        Name is { Length: > 0 and <= 64 } && Name.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')
        && !string.IsNullOrWhiteSpace(Title) && Title.Length <= 128 && !Title.Any(char.IsControl);

    internal bool ToolsAreValid() =>
        ExposedTypes is not null && ExposedTypes.All(type => !string.IsNullOrWhiteSpace(type))
        && MaxExposedTypes is >= 0 and <= 100
        && UploadTimeout > TimeSpan.Zero && UploadTimeout <= TimeSpan.FromMinutes(10);

    internal bool SafetyIsValid() =>
        MaxBulkItems is >= 1 and <= Content.BulkRequest.MaxItems
        && MaxRequestBytes >= MinBytes && MaxResultBytes >= MinBytes && MaxListTextLength >= MinListTextLength
        && Limits is { CallsPerMinute: >= 1, WritesPerMinute: >= 1 } && Limits.WritesPerMinute <= Limits.CallsPerMinute;
}
