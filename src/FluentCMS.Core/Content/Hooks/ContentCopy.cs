using System.Text.Json.Nodes;
using FluentCMS.Content;

namespace FluentCMS.Core.Content.Hooks;

// A copy of an entry that no hook can use to reach the original: documents, dictionaries, object lists and JSON are copied
// all the way down. Immutable values (strings, numbers, references, rich text, block lists) are shared.
internal static class ContentCopy
{
    public static ContentDocument Of(ContentDocument document)
    {
        var copy = new ContentDocument();
        foreach (var (name, value) in document)
        {
            copy[name] = Value(value);
        }

        return copy;
    }

    private static object? Value(object? value) => value switch
    {
        ContentDocument document => Of(document),
        JsonNode node => node.DeepClone(),
        IDictionary<string, object?> map => map.ToDictionary(item => item.Key, item => Value(item.Value), StringComparer.Ordinal),
        IReadOnlyDictionary<string, object?> map => map.ToDictionary(item => item.Key, item => Value(item.Value), StringComparer.Ordinal),
        List<object?> list => list.ConvertAll(Value),
        object?[] array => Array.ConvertAll(array, Value),
        _ => value,
    };
}
