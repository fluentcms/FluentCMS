using System.Text.Json;

namespace FluentCMS.Core.Schema;

// Compares two snapshots in the form SchemaJsonWriter writes. A field that is new and lists the name of a field that is
// gone in its formerNames is a rename, which is why the snapshot keeps formerNames.
internal static class SchemaSnapshotDiff
{
    private static readonly string[] Groups = ["collections", "singletons", "components", "blocks"];

    public static IReadOnlyList<SchemaChange> Compare(string before, string after)
    {
        using var previous = JsonDocument.Parse(before);
        using var current = JsonDocument.Parse(after);

        var oldTypes = TypesOf(previous.RootElement);
        var newTypes = TypesOf(current.RootElement);
        var changes = new List<SchemaChange>();

        changes.AddRange(oldTypes.Keys.Where(name => !newTypes.ContainsKey(name)).Select(name => new SchemaChange(SchemaChangeKind.TypeRemoved, name)));

        foreach (var (name, type) in newTypes)
        {
            if (oldTypes.TryGetValue(name, out var old))
            {
                CompareType(name, old, type, changes);
            }
            else
            {
                changes.Add(new SchemaChange(SchemaChangeKind.TypeAdded, name));
            }
        }

        return changes;
    }

    private static SortedDictionary<string, (string Group, JsonElement Element)> TypesOf(JsonElement root)
    {
        var types = new SortedDictionary<string, (string Group, JsonElement Element)>(StringComparer.Ordinal);
        foreach (var group in Groups)
        {
            if (!root.TryGetProperty(group, out var list))
            {
                continue;
            }

            foreach (var type in list.EnumerateArray())
            {
                types[type.GetProperty("name").GetString()!] = (group, type);
            }
        }

        return types;
    }

    private static void CompareType(string name, (string Group, JsonElement Element) oldType, (string Group, JsonElement Element) newType, List<SchemaChange> changes)
    {
        var (old, current) = (oldType.Element, newType.Element);
        var typeChanges = Differences(old, current, "name", "fields");
        if (oldType.Group != newType.Group)
        {
            typeChanges.Insert(0, "kind");
        }

        if (typeChanges.Count > 0)
        {
            changes.Add(new SchemaChange(SchemaChangeKind.TypeChanged, name, Properties: typeChanges));
        }

        var oldFields = FieldsOf(old);
        var newFields = FieldsOf(current);
        var renamedFrom = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (field, element) in newFields)
        {
            if (oldFields.TryGetValue(field, out var oldField))
            {
                var fieldChanges = Differences(oldField, element, "name");
                if (fieldChanges.Count > 0)
                {
                    changes.Add(new SchemaChange(SchemaChangeKind.FieldChanged, name, field, Properties: fieldChanges));
                }

                continue;
            }

            var former = FormerNamesOf(element).FirstOrDefault(candidate => oldFields.ContainsKey(candidate) && !newFields.ContainsKey(candidate) && !renamedFrom.Contains(candidate));
            if (former is null)
            {
                changes.Add(new SchemaChange(SchemaChangeKind.FieldAdded, name, field));
                continue;
            }

            renamedFrom.Add(former);
            changes.Add(new SchemaChange(SchemaChangeKind.FieldRenamed, name, field, former));

            var renamedChanges = Differences(oldFields[former], element, "name", "formerNames");
            if (renamedChanges.Count > 0)
            {
                changes.Add(new SchemaChange(SchemaChangeKind.FieldChanged, name, field, Properties: renamedChanges));
            }
        }

        foreach (var field in oldFields.Keys.Where(field => !newFields.ContainsKey(field) && !renamedFrom.Contains(field)))
        {
            changes.Add(new SchemaChange(SchemaChangeKind.FieldRemoved, name, field));
        }

        var kept = oldFields.Keys.Where(newFields.ContainsKey).ToList();
        if (!kept.SequenceEqual(newFields.Keys.Where(oldFields.ContainsKey), StringComparer.Ordinal))
        {
            changes.Add(new SchemaChange(SchemaChangeKind.FieldsReordered, name));
        }
    }

    // In declaration order, which is what the order of the dictionary keeps.
    private static Dictionary<string, JsonElement> FieldsOf(JsonElement type)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (type.TryGetProperty("fields", out var list))
        {
            foreach (var field in list.EnumerateArray())
            {
                fields[field.GetProperty("name").GetString()!] = field;
            }
        }

        return fields;
    }

    private static IEnumerable<string> FormerNamesOf(JsonElement field) =>
        field.TryGetProperty("formerNames", out var names) ? names.EnumerateArray().Select(name => name.GetString()!) : [];

    // The keys whose value differs or that only one side has, sorted.
    private static List<string> Differences(JsonElement old, JsonElement current, params string[] ignored)
    {
        var keys = old.EnumerateObject().Select(property => property.Name)
            .Union(current.EnumerateObject().Select(property => property.Name))
            .Where(key => !ignored.Contains(key))
            .Order(StringComparer.Ordinal);

        return [.. keys.Where(key => Raw(old, key) != Raw(current, key))];
    }

    private static string? Raw(JsonElement element, string key) =>
        element.TryGetProperty(key, out var value) ? value.GetRawText() : null;
}
