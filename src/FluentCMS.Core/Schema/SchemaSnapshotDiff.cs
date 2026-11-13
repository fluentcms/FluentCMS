using System.Text.Json;

namespace FluentCMS.Core.Schema;

// Compares two snapshots in the form SchemaJsonWriter writes. A field that is new and lists the name of a field that is
// gone in its formerNames is a rename, which is why the snapshot keeps formerNames. A snapshot read from a database can
// be anything, so a shape other than the writer's throws JsonException and nothing else.
internal static class SchemaSnapshotDiff
{
    public const string ContentFamily = "type";
    public const string ComponentFamily = "component";
    public const string BlockFamily = "block";

    private static readonly (string Group, string Family)[] Groups =
    [
        ("collections", ContentFamily),
        ("singletons", ContentFamily),
        ("components", ComponentFamily),
        ("blocks", BlockFamily),
    ];

    public static IReadOnlyList<SchemaChange> Compare(string before, string after) => Report(before, after).Changes;

    // The changes and the possible renames among them, which the schema tools print and the drift log leaves out.
    public static SchemaDiffReport Report(string before, string after)
    {
        try
        {
            var renames = new List<PossibleRename>();
            return new SchemaDiffReport(CompareCore(before, after, renames), renames);
        }
        catch (InvalidOperationException)
        {
            // GetString on a string with a lone surrogate, and whatever else a text that is JSON can do to a reader.
            throw Unexpected();
        }
    }

    private static List<SchemaChange> CompareCore(string before, string after, List<PossibleRename> renames)
    {
        using var previous = JsonDocument.Parse(before);
        using var current = JsonDocument.Parse(after);

        var oldTypes = TypesOf(previous.RootElement);
        var newTypes = TypesOf(current.RootElement);
        var changes = new List<SchemaChange>();

        changes.AddRange(oldTypes.Keys.Where(key => !newTypes.ContainsKey(key)).Select(key => new SchemaChange(SchemaChangeKind.TypeRemoved, key.Family, key.Name)));

        foreach (var (key, type) in newTypes)
        {
            if (oldTypes.TryGetValue(key, out var old))
            {
                CompareType(key, old, type, changes, renames);
            }
            else
            {
                changes.Add(new SchemaChange(SchemaChangeKind.TypeAdded, key.Family, key.Name));
            }
        }

        return changes;
    }

    private static SortedDictionary<(string Family, string Name), (string Group, JsonElement Element)> TypesOf(JsonElement root)
    {
        Require(root, JsonValueKind.Object);

        var types = new SortedDictionary<(string Family, string Name), (string Group, JsonElement Element)>();
        foreach (var (group, family) in Groups)
        {
            if (!root.TryGetProperty(group, out var list))
            {
                continue;
            }

            Require(list, JsonValueKind.Array);
            foreach (var type in list.EnumerateArray())
            {
                Require(type, JsonValueKind.Object);
                types[(family, NameOf(type))] = (group, type);
            }
        }

        return types;
    }

    private static void CompareType((string Family, string Name) key, (string Group, JsonElement Element) oldType, (string Group, JsonElement Element) newType, List<SchemaChange> changes, List<PossibleRename> renames)
    {
        var (family, name) = key;
        var (old, current) = (oldType.Element, newType.Element);
        var typeChanges = Differences(old, current, "name", "fields");
        if (oldType.Group != newType.Group)
        {
            typeChanges.Insert(0, "kind");
        }

        if (typeChanges.Count > 0)
        {
            changes.Add(new SchemaChange(SchemaChangeKind.TypeChanged, family, name, Properties: typeChanges));
        }

        var oldFields = FieldsOf(old);
        var newFields = FieldsOf(current);
        var renamedFrom = new HashSet<string>(StringComparer.Ordinal);
        var added = new List<string>();

        foreach (var (field, element) in newFields)
        {
            if (oldFields.TryGetValue(field, out var oldField))
            {
                var fieldChanges = Differences(oldField, element, "name");
                if (fieldChanges.Count > 0)
                {
                    changes.Add(new SchemaChange(SchemaChangeKind.FieldChanged, family, name, field, Properties: fieldChanges));
                }

                continue;
            }

            var former = FormerNamesOf(element).FirstOrDefault(candidate => oldFields.ContainsKey(candidate) && !newFields.ContainsKey(candidate) && !renamedFrom.Contains(candidate));
            if (former is null)
            {
                changes.Add(new SchemaChange(SchemaChangeKind.FieldAdded, family, name, field));
                added.Add(field);
                continue;
            }

            renamedFrom.Add(former);
            changes.Add(new SchemaChange(SchemaChangeKind.FieldRenamed, family, name, field, former));

            var renamedChanges = Differences(oldFields[former], element, "name", "formerNames");
            if (renamedChanges.Count > 0)
            {
                changes.Add(new SchemaChange(SchemaChangeKind.FieldChanged, family, name, field, Properties: renamedChanges));
            }
        }

        var removed = oldFields.Keys.Where(field => !newFields.ContainsKey(field) && !renamedFrom.Contains(field)).ToList();
        foreach (var field in removed)
        {
            changes.Add(new SchemaChange(SchemaChangeKind.FieldRemoved, family, name, field));
        }

        // Each added field takes the first removed field of its kind that no other took, in declaration order.
        foreach (var field in added)
        {
            var match = removed.FirstOrDefault(candidate => SameKind(oldFields[candidate], newFields[field]));
            if (match is not null)
            {
                removed.Remove(match);
                renames.Add(new PossibleRename(family, name, match, field));
            }
        }

        var kept = oldFields.Keys.Where(newFields.ContainsKey).ToList();
        if (!kept.SequenceEqual(newFields.Keys.Where(oldFields.ContainsKey), StringComparer.Ordinal))
        {
            changes.Add(new SchemaChange(SchemaChangeKind.FieldsReordered, family, name));
        }
    }

    // The kind of a field is its field type and whether it holds a list, which is what its storage follows.
    private static bool SameKind(JsonElement old, JsonElement current) =>
        Raw(old, "type") == Raw(current, "type") && Raw(old, "multiple") == Raw(current, "multiple");

    // In declaration order, which is what the order of the dictionary keeps.
    private static Dictionary<string, JsonElement> FieldsOf(JsonElement type)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (type.TryGetProperty("fields", out var list))
        {
            Require(list, JsonValueKind.Array);
            foreach (var field in list.EnumerateArray())
            {
                Require(field, JsonValueKind.Object);
                fields[NameOf(field)] = field;
            }
        }

        return fields;
    }

    private static IEnumerable<string> FormerNamesOf(JsonElement field)
    {
        if (!field.TryGetProperty("formerNames", out var names))
        {
            return [];
        }

        Require(names, JsonValueKind.Array);
        return [.. names.EnumerateArray().Select(name => name.ValueKind == JsonValueKind.String ? name.GetString()! : throw Unexpected())];
    }

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

    private static string NameOf(JsonElement element) =>
        element.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString()! : throw Unexpected();

    private static void Require(JsonElement element, JsonValueKind kind)
    {
        if (element.ValueKind != kind)
        {
            throw Unexpected();
        }
    }

    private static JsonException Unexpected() => new("The snapshot does not have the shape of a schema.");
}
