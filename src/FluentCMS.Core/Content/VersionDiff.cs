using System.Globalization;
using System.Text.Json.Nodes;
using FluentCMS.Publishing;

namespace FluentCMS.Core.Content;

// The structural diff of spec 4.5, over two sets of fields. A list whose items all carry an id on both sides, as stored blocks
// do (ADR 0002), pairs them by id; any other list pairs them by position. An item of another kind (a block or a rich text node
// with another "type") is one change, not one per field. No value and null are the same.
internal static class VersionDiff
{
    private const string Discriminator = "type";
    private const string Id = "id";

    public static List<VersionChange> Between(JsonObject before, JsonObject after)
    {
        List<VersionChange> changes = [];
        Compare("", before, after, changes);
        return changes;
    }

    private static void Compare(string path, JsonNode? before, JsonNode? after, List<VersionChange> changes)
    {
        if (JsonNode.DeepEquals(before, after))
        {
            return;
        }

        switch (before, after)
        {
            case (null, _):
                changes.Add(new VersionChange(path, VersionChangeKind.Added, null, after!.DeepClone()));
                break;
            case (_, null):
                changes.Add(new VersionChange(path, VersionChangeKind.Removed, before.DeepClone(), null));
                break;
            case (JsonObject left, JsonObject right) when JsonNode.DeepEquals(left[Discriminator], right[Discriminator]):
                CompareObjects(path, left, right, changes);
                break;
            case (JsonArray left, JsonArray right) when IdsOf(left) is { } leftIds && IdsOf(right) is { } rightIds:
                CompareById(path, left, right, leftIds, rightIds, changes);
                break;
            case (JsonArray left, JsonArray right):
                for (var index = 0; index < Math.Max(left.Count, right.Count); index++)
                {
                    Compare(Item(path, index), index < left.Count ? left[index] : null, index < right.Count ? right[index] : null, changes);
                }

                break;
            default:
                changes.Add(new VersionChange(path, VersionChangeKind.Changed, before.DeepClone(), after.DeepClone()));
                break;
        }
    }

    private static void CompareObjects(string path, JsonObject before, JsonObject after, List<VersionChange> changes)
    {
        var prefix = path.Length == 0 ? "" : path + ".";
        foreach (var (key, value) in before)
        {
            Compare(prefix + key, value, after[key], changes);
        }

        foreach (var (key, value) in after)
        {
            if (!before.ContainsKey(key))
            {
                Compare(prefix + key, null, value, changes);
            }
        }
    }

    // An item kept in its order is compared where it is now. One that is new, gone or moved is added at its new index and
    // removed at its old one, so a block inserted at the top is one change and does not move every block after it.
    private static void CompareById(string path, JsonArray before, JsonArray after, string[] beforeIds, string[] afterIds, List<VersionChange> changes)
    {
        var at = beforeIds.Select((id, index) => (id, index)).ToDictionary(item => item.id, item => item.index, StringComparer.Ordinal);
        var paired = afterIds.Select(id => at.GetValueOrDefault(id, -1)).ToArray();
        var kept = InOrder(paired);

        for (var index = 0; index < after.Count; index++)
        {
            if (kept.Contains(index))
            {
                Compare(Item(path, index), before[paired[index]], after[index], changes);
            }
            else
            {
                changes.Add(new VersionChange(Item(path, index), VersionChangeKind.Added, null, after[index]!.DeepClone()));
            }
        }

        var stayed = kept.Select(index => paired[index]).ToHashSet();
        for (var index = 0; index < before.Count; index++)
        {
            if (!stayed.Contains(index))
            {
                changes.Add(new VersionChange(Item(path, index), VersionChangeKind.Removed, before[index]!.DeepClone(), null));
            }
        }
    }

    // The indexes of after whose items keep their order relative to each other: the longest run of paired items whose earlier
    // indexes increase, found in n log n so that a long list costs no more than sorting it. paired holds the earlier index of
    // each item, or -1 for one with no pair.
    private static HashSet<int> InOrder(int[] paired)
    {
        // tails[k] is the index in after that ends the best run of length k + 1 found so far.
        List<int> tails = [];
        var previous = new int[paired.Length];
        for (var index = 0; index < paired.Length; index++)
        {
            if (paired[index] < 0)
            {
                continue;
            }

            int low = 0, high = tails.Count;
            while (low < high)
            {
                var middle = (low + high) / 2;
                if (paired[tails[middle]] < paired[index])
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            previous[index] = low > 0 ? tails[low - 1] : -1;
            if (low == tails.Count)
            {
                tails.Add(index);
            }
            else
            {
                tails[low] = index;
            }
        }

        HashSet<int> kept = [];
        for (var index = tails.Count > 0 ? tails[^1] : -1; index >= 0; index = previous[index])
        {
            kept.Add(index);
        }

        return kept;
    }

    // The ids of a list's items, or null unless every item is an object with an id of its own.
    private static string[]? IdsOf(JsonArray list)
    {
        var ids = new string[list.Count];
        HashSet<string> seen = new(StringComparer.Ordinal);
        for (var index = 0; index < list.Count; index++)
        {
            if (list[index] is not JsonObject item || item[Id] is not JsonValue value || !value.TryGetValue<string>(out var id) || !seen.Add(id))
            {
                return null;
            }

            ids[index] = id;
        }

        return ids;
    }

    private static string Item(string path, int index) => string.Create(CultureInfo.InvariantCulture, $"{path}[{index}]");
}
