using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Content.Validation;
using FluentCMS.Schema;

namespace FluentCMS.Core.Content.Hooks;

public static class ContentChanges
{
    private static readonly IReadOnlyDictionary<string, object?> NoValues = new Dictionary<string, object?>();

    // The paths of the fields whose values differ, in the form validation reports: seo.metaTitle, sections[2].heading, tags[1].
    // A missing field counts as null, so a create (no previous state) lists the fields that hold a value. Lists of the same
    // length are compared by index; a list whose length changed is reported as the list itself. Components and blocks are
    // compared field by field whether they are dictionaries, JSON objects or instances of a component or block class;
    // JSON values compare as the scalars they hold, and rich text as the JSON document it holds. Every other value compares by
    // value as a schema stores it (a Ref by its id) or, failing that, by Equals (MediaRef).
    public static IReadOnlyList<string> Between(IReadOnlyDictionary<string, object?>? previous, IReadOnlyDictionary<string, object?> current)
    {
        ArgumentNullException.ThrowIfNull(current);

        var paths = new List<string>();
        CompareMaps(previous ?? NoValues, current, "", paths);
        return paths;
    }

    private static void CompareMaps(IReadOnlyDictionary<string, object?> previous, IReadOnlyDictionary<string, object?> current, string prefix, List<string> paths)
    {
        foreach (var (name, value) in current)
        {
            previous.TryGetValue(name, out var before);
            CompareValues(before, value, Join(prefix, name), paths);
        }

        foreach (var (name, value) in previous)
        {
            if (!current.ContainsKey(name))
            {
                CompareValues(value, null, Join(prefix, name), paths);
            }
        }
    }

    private static void CompareValues(object? before, object? after, string path, List<string> paths)
    {
        before = Plain(before);
        after = Plain(after);

        if (IsInstance(before) && IsInstance(after) && before!.GetType() != after!.GetType())
        {
            paths.Add(path);
        }
        else if (AsMap(before) is { } beforeMap && AsMap(after) is { } afterMap)
        {
            CompareMaps(beforeMap, afterMap, path, paths);
        }
        else if (AsList(before) is { } beforeList && AsList(after) is { } afterList)
        {
            if (beforeList.Count != afterList.Count)
            {
                paths.Add(path);
                return;
            }

            for (var index = 0; index < afterList.Count; index++)
            {
                CompareValues(beforeList[index], afterList[index], $"{path}[{index}]", paths);
            }
        }
        else if (before is RichText beforeText && after is RichText afterText)
        {
            if (!EntryJson.SameDocument(beforeText, afterText))
            {
                paths.Add(path);
            }
        }
        else if (!Equals(FieldValue.Normalize(before), FieldValue.Normalize(after)))
        {
            paths.Add(path);
        }
    }

    private static string Join(string prefix, string name) => prefix.Length == 0 ? name : $"{prefix}.{name}";

    // JSON as plain values: dictionaries, lists, strings, booleans, longs and decimals.
    private static object? Plain(object? value) => value switch
    {
        JsonElement { ValueKind: JsonValueKind.Undefined } => null,
        JsonElement element => Plain(JsonDepth.Parse(element.GetRawText())),
        JsonObject node => node.ToDictionary(property => property.Key, property => Plain(property.Value), StringComparer.Ordinal),
        JsonArray node => node.Select(Plain).ToList(),
        JsonValue node => PlainScalar(node),
        _ => value,
    };

    private static object? PlainScalar(JsonValue node)
    {
        switch (node.GetValueKind())
        {
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Number:
                if (node.TryGetValue(out long whole))
                {
                    return whole;
                }

                return node.TryGetValue(out decimal number) ? number : node.ToJsonString();
            case JsonValueKind.String:
                return node.TryGetValue(out string? text) ? text : node.ToJsonString();
            default:
                return null;
        }
    }

    private static bool IsInstance(object? value) =>
        value is not null && (Attribute.IsDefined(value.GetType(), typeof(BlockAttribute)) || Attribute.IsDefined(value.GetType(), typeof(ComponentAttribute)));

    private static IReadOnlyDictionary<string, object?>? AsMap(object? value) => value switch
    {
        IReadOnlyDictionary<string, object?> map => map,
        IDictionary<string, object?> map => new Dictionary<string, object?>(map),
        _ when IsInstance(value) => FieldValue.ReadInstance(value),
        _ => null,
    };

    private static List<object?>? AsList(object? value) =>
        value is IEnumerable and not string and not IReadOnlyDictionary<string, object?> and not IDictionary<string, object?>
            ? [.. ((IEnumerable)value).Cast<object?>()]
            : null;
}
