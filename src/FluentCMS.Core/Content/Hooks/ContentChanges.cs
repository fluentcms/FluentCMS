using System.Collections;
using FluentCMS.Core.Content.Validation;

namespace FluentCMS.Core.Content.Hooks;

public static class ContentChanges
{
    private static readonly IReadOnlyDictionary<string, object?> NoValues = new Dictionary<string, object?>();

    // The paths of the fields whose values differ, in the form validation reports: seo.metaTitle, sections[2].heading, tags[1].
    // A missing field counts as null, so a create (no previous state) lists the fields that hold a value. Lists of the same
    // length are compared by index; a list whose length changed is reported as the list itself. Values are API-shaped:
    // dictionaries for components and blocks, and scalars compared as a schema stores them.
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
        if (AsMap(before) is { } beforeMap && AsMap(after) is { } afterMap)
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
        else if (!Equals(FieldValue.Normalize(before), FieldValue.Normalize(after)))
        {
            paths.Add(path);
        }
    }

    private static string Join(string prefix, string name) => prefix.Length == 0 ? name : $"{prefix}.{name}";

    private static IReadOnlyDictionary<string, object?>? AsMap(object? value) => value switch
    {
        IReadOnlyDictionary<string, object?> map => map,
        IDictionary<string, object?> map => new Dictionary<string, object?>(map),
        _ => null,
    };

    private static List<object?>? AsList(object? value) =>
        value is IEnumerable and not string and not IReadOnlyDictionary<string, object?> and not IDictionary<string, object?>
            ? [.. ((IEnumerable)value).Cast<object?>()]
            : null;
}
