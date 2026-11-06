using System.Collections.Immutable;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FluentCMS.Core.ProseMirror;

internal static partial class RichTextAttributes
{
    // A list marker has at most nine digits in Markdown.
    private const int MaxListStart = 999_999_999;

    public static readonly ImmutableSortedDictionary<string, JsonElement> Empty =
        ImmutableSortedDictionary.Create<string, JsonElement>(StringComparer.Ordinal);

    public static ImmutableSortedDictionary<string, JsonElement> Create(IEnumerable<KeyValuePair<string, JsonElement>>? attributes) =>
        attributes is null
            ? Empty
            : ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, attributes);

    public static ImmutableSortedDictionary<string, JsonElement> Create(params (string Name, JsonElement Value)[] attributes) =>
        Create(attributes.Select(pair => KeyValuePair.Create(pair.Name, pair.Value)));

    public static JsonElement Number(int value) => JsonSerializer.SerializeToElement(value);

    public static JsonElement String(string? value) => JsonSerializer.SerializeToElement(value);

    public static string? CheckHeadingLevel(int level) =>
        level is >= 1 and <= 6 ? null : "The heading level must be between 1 and 6.";

    public static string? CheckListStart(int start) =>
        start is >= 0 and <= MaxListStart ? null : $"The start must be between 0 and {MaxListStart}.";

    public static string? CheckBlockType(string? blockType)
    {
        if (blockType is null || !NamePattern().IsMatch(blockType))
        {
            return "The block type must start with a letter and contain only letters, digits, '-' and '_'.";
        }

        return NodeSpec.IsReserved(blockType) ? $"The block type '{blockType}' is a reserved node type name." : null;
    }

    public static string? CheckEntryType(string? entryType) =>
        entryType is not null && EntryTypePattern().IsMatch(entryType)
            ? null
            : "The entry type must be a lower case kebab-case content type name.";

    // The id of a block or an inline block: 1 to 64 URL-safe characters, so that imported ids survive.
    public static string? CheckBlockId(string? id) =>
        id is not null && BlockIdPattern().IsMatch(id)
            ? null
            : "The block id must be 1 to 64 letters, digits, '-' or '_'.";

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]*$")]
    private static partial Regex NamePattern();

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex BlockIdPattern();

    [GeneratedRegex("^[a-z][a-z0-9-]*$")]
    private static partial Regex EntryTypePattern();
}
