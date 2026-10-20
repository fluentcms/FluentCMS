using System.Text.Json;

namespace FluentCMS.Core.ChangeSets;

// One named value that differs between two versions of an item (CHG-03). Before is absent for an added value and After for a
// removed one. Not a record: the values are JSON, which has no value equality.
public sealed class ValueDiff
{
    private ValueDiff(string name, ValueChange change, JsonElement? before, JsonElement? after)
    {
        Name = name;
        Change = change;
        // A clone outlives the document it was read from.
        Before = before?.Clone();
        After = after?.Clone();
    }

    public string Name { get; }

    public ValueChange Change { get; }

    public JsonElement? Before { get; }

    public JsonElement? After { get; }

    // A diff read back from where it was kept, such as the state the set view carries into its circuit.
    public static ValueDiff Restore(string name, ValueChange change, JsonElement? before, JsonElement? after)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return new ValueDiff(name, change, before, after);
    }

    // Values are matched by exact name and compared as JSON, so property order and number formatting are not changes. The result
    // follows the order of the later version, then lists the removed values in the order of the earlier one.
    public static IReadOnlyList<ValueDiff> Compare(
        IEnumerable<KeyValuePair<string, JsonElement>> before,
        IEnumerable<KeyValuePair<string, JsonElement>> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var earlier = before.ToList();
        var later = after.ToList();
        var earlierByName = earlier.ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal);
        var laterNames = later.Select(value => value.Key).ToHashSet(StringComparer.Ordinal);
        var diffs = new List<ValueDiff>();

        foreach (var (name, value) in later)
        {
            if (!earlierByName.TryGetValue(name, out var previous))
            {
                diffs.Add(new ValueDiff(name, ValueChange.Added, null, value));
            }
            else if (!JsonElement.DeepEquals(previous, value))
            {
                diffs.Add(new ValueDiff(name, ValueChange.Changed, previous, value));
            }
        }

        diffs.AddRange(earlier
            .Where(value => !laterNames.Contains(value.Key))
            .Select(value => new ValueDiff(value.Key, ValueChange.Removed, value.Value, null)));

        return diffs;
    }
}
