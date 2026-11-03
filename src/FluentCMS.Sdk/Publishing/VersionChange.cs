using System.Text.Json.Nodes;

namespace FluentCMS.Publishing;

/// <summary>One difference between two versions of an entry.</summary>
/// <param name="Path">Where the value is, such as <c>sections[1].heading</c> or <c>body.content[0].content[0].text</c>.</param>
/// <param name="Kind">How it differs.</param>
/// <param name="Before">The value in the earlier version.</param>
/// <param name="After">The value in the later version.</param>
public sealed record VersionChange(string Path, VersionChangeKind Kind, JsonNode? Before, JsonNode? After);
