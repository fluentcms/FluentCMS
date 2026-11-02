using System.Collections.Immutable;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FluentCMS.Core.ProseMirror;

internal static partial class RichTextAttributes
{
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

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]*$")]
    private static partial Regex NamePattern();

    [GeneratedRegex("^[a-z][a-z0-9-]*$")]
    private static partial Regex EntryTypePattern();
}
