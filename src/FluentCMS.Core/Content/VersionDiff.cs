using System.Globalization;
using System.Text.Json.Nodes;
using FluentCMS.Publishing;

namespace FluentCMS.Core.Content;

// The structural diff of spec 4.5, over two sets of fields. A list has no ids, so items compare by position. An item of
// another kind (a block or a rich text node with another "type") is one change, not one per field. No value and null are the same.
internal static class VersionDiff
{
    private const string Discriminator = "type";

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
            case (JsonArray left, JsonArray right):
                for (var index = 0; index < Math.Max(left.Count, right.Count); index++)
                {
                    Compare(
                        string.Create(CultureInfo.InvariantCulture, $"{path}[{index}]"),
                        index < left.Count ? left[index] : null,
                        index < right.Count ? right[index] : null,
                        changes);
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
}
