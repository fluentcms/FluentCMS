using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Schema;

namespace FluentCMS.Core.Content;

// Makes the entry an update asks for out of what is stored and what the client sent, before it is canonicalized.
internal sealed class EntryComposer(SchemaModel schema)
{
    private const string Discriminator = "type";

    // A full update describes the whole entry, but only as far as the client can see and change it: keep says which
    // fields it has no say over. A key that is not a field stays, so that the canonicalizer refuses it.
    public static ContentDocument Replace(SchemaType type, ContentDocument stored, ContentDocument incoming, Func<SchemaField, bool> keep)
    {
        ContentDocument composed = [];
        foreach (var field in type.Fields)
        {
            if (incoming.TryGetValue(field.Name, out var value))
            {
                composed[field.Name] = value;
            }
            else if (keep(field))
            {
                stored.TryGetValue(field.Name, out var kept);
                composed[field.Name] = kept;
            }
        }

        foreach (var (key, value) in incoming)
        {
            if (type.FindField(key) is null && SystemField.Find(key) is null)
            {
                composed[key] = value;
            }
        }

        return composed;
    }

    // RFC 7396: a null removes, an object merges into a single component or a JSON field, and anything else replaces.
    // A block list and a repeated component are arrays, so they replace, and rich text is one document.
    public ContentDocument Patch(SchemaType type, ContentDocument stored, ContentDocument patch)
    {
        ContentDocument composed = [];
        foreach (var field in type.Fields)
        {
            stored.TryGetValue(field.Name, out var value);
            composed[field.Name] = value;
        }

        foreach (var (key, value) in patch)
        {
            if (SystemField.Find(key) is null)
            {
                composed[key] = type.FindField(key) is { } field ? Merge(field, stored.GetValueOrDefault(key), value) : value;
            }
        }

        return composed;
    }

    private object? Merge(SchemaField field, object? stored, object? patch)
    {
        if (RawValue.Unwrap(patch) is null)
        {
            return null;
        }

        if (field.FieldType == FieldTypeNames.Component && !field.Multiple && EntryCanonicalizer.TryReadMap(patch, out var map) && map is not null
            && schema.FindComponent((string)field.Settings[FieldSettings.Component]) is { } component)
        {
            ContentDocument group = stored as ContentDocument is { } existing ? new(existing) : [];
            foreach (var (key, value) in map)
            {
                group[key] = component.FindField(key) is { } inner ? Merge(inner, group.GetValueOrDefault(key), value) : value;
            }

            return group;
        }

        if (field.FieldType == FieldTypeNames.Json && RawValue.TryReadJson(patch, out var node, out _) && node is JsonObject changes)
        {
            return MergeJson(stored as JsonNode, changes);
        }

        return patch;
    }

    private static JsonObject MergeJson(JsonNode? stored, JsonObject patch)
    {
        var result = stored is JsonObject existing ? (JsonObject)existing.DeepClone() : [];
        foreach (var (key, value) in patch)
        {
            switch (value)
            {
                case null:
                    result.Remove(key);
                    break;
                case JsonObject inner:
                    result[key] = MergeJson(result[key], inner);
                    break;
                default:
                    result[key] = value.DeepClone();
                    break;
            }
        }

        return result;
    }

    // What a client cannot see or change inside a component or a block stays when it sends the rest back, even when it
    // sends no component at all. A list has no ids, so an incoming block keeps the values of the stored block of the
    // same type whose visible content is identical, each used once, and what is left pairs up in order. A block both
    // moved and edited can still pair with the wrong one. A single component is left alone on a patch only for the
    // system principal, which may be clearing a value, since a patch merged into a component already keeps the rest.
    public void CarryProtected(SchemaType type, ContentDocument stored, ContentDocument composed, bool includingGroups)
    {
        foreach (var field in type.Fields)
        {
            stored.TryGetValue(field.Name, out var before);
            composed.TryGetValue(field.Name, out var after);
            if (field.FieldType == FieldTypeNames.Component && !field.Multiple)
            {
                if (includingGroups && before is ContentDocument oldGroup && ComponentOf(field) is { } component)
                {
                    CarryGroup(component, oldGroup, after as ContentDocument, group => composed[field.Name] = group);
                }
            }
            else if (before is JsonNode oldNode && after is JsonNode newNode)
            {
                CarryInside(field, oldNode, newNode);
            }
        }
    }

    private SchemaType? ComponentOf(SchemaField field) =>
        field.Settings.TryGetValue(FieldSettings.Component, out var name) && name is string component ? schema.FindComponent(component) : null;

    // An absent component comes back only if it has something to carry.
    private void CarryGroup(SchemaType component, ContentDocument before, ContentDocument? after, Action<ContentDocument> set)
    {
        var group = after ?? [];
        foreach (var field in component.Fields)
        {
            var old = before.GetValueOrDefault(field.Name);
            var current = group.GetValueOrDefault(field.Name);
            if ((field.Hidden || field.ReadOnly) && old is not null && current is null)
            {
                group[field.Name] = old;
            }
            else if (old is JsonNode oldNode && current is JsonNode newNode)
            {
                CarryInside(field, oldNode, newNode);
            }
            else if (field.FieldType == FieldTypeNames.Component && !field.Multiple && old is ContentDocument oldGroup && ComponentOf(field) is { } inner)
            {
                CarryGroup(inner, oldGroup, current as ContentDocument, value => group[field.Name] = value);
            }
        }

        if (after is null && group.Values.Any(value => value is not null))
        {
            set(group);
        }
    }

    private void CarryInside(SchemaField field, JsonNode before, JsonNode after)
    {
        if (before is JsonArray oldItems && after is JsonArray newItems)
        {
            foreach (var (oldItem, newItem) in PairUp(field, [.. oldItems.OfType<JsonObject>()], [.. newItems.OfType<JsonObject>()]))
            {
                if (OwnerOf(field, oldItem) is { } owner)
                {
                    CarryIntoObject(owner, oldItem, newItem);
                }
            }
        }
        else if (before is JsonObject oldObject && after is JsonObject newObject && OwnerOf(field, oldObject) is { } single && OwnerOf(field, newObject) == single)
        {
            CarryIntoObject(single, oldObject, newObject);
        }
    }

    private List<(JsonObject Old, JsonObject New)> PairUp(SchemaField field, List<JsonObject> before, List<JsonObject> after)
    {
        List<(JsonObject Old, JsonObject New)> pairs = [];
        List<JsonObject> oldLeft = [.. before];
        List<JsonObject> newLeft = [];
        foreach (var item in after)
        {
            var same = OwnerOf(field, item) is { } owner
                ? oldLeft.FindIndex(old => OwnerOf(field, old) == owner && JsonNode.DeepEquals(Comparable(owner, old), Comparable(owner, item)))
                : -1;

            if (same < 0)
            {
                newLeft.Add(item);
            }
            else
            {
                pairs.Add((oldLeft[same], item));
                oldLeft.RemoveAt(same);
            }
        }

        for (var index = 0; index < Math.Min(oldLeft.Count, newLeft.Count); index++)
        {
            if (OwnerOf(field, oldLeft[index]) is { } owner && OwnerOf(field, newLeft[index]) == owner)
            {
                pairs.Add((oldLeft[index], newLeft[index]));
            }
        }

        return pairs;
    }

    // What a client saw of a block: no protected values, and no key that holds nothing, which is the same as no key.
    private JsonObject Comparable(SchemaType owner, JsonObject item)
    {
        JsonObject result = [];
        foreach (var (key, value) in item)
        {
            if (value is null || owner.FindField(key) is { Hidden: true } or { ReadOnly: true })
            {
                continue;
            }

            var inner = owner.FindField(key) is { } field ? ComparableValue(field, value) : value.DeepClone();
            if (inner is not JsonObject { Count: 0 })
            {
                result[key] = inner;
            }
        }

        return result;
    }

    private JsonNode ComparableValue(SchemaField field, JsonNode value) => value switch
    {
        JsonObject single when OwnerOf(field, single) is { } owner => Comparable(owner, single),
        JsonArray items => new JsonArray([.. items.Select(item => item is null ? null : ComparableValue(field, item))]),
        _ => value.DeepClone(),
    };

    private SchemaType? OwnerOf(SchemaField field, JsonObject item) => field.FieldType switch
    {
        FieldTypeNames.Component => ComponentOf(field),
        FieldTypeNames.Blocks when item[Discriminator] is JsonValue name && name.TryGetValue<string>(out var block) => schema.FindBlock(block),
        _ => null,
    };

    private void CarryIntoObject(SchemaType owner, JsonObject before, JsonObject after)
    {
        foreach (var field in owner.Fields)
        {
            if (before[field.Name] is not { } value)
            {
                continue;
            }

            if (field.Hidden || field.ReadOnly)
            {
                if (!after.ContainsKey(field.Name))
                {
                    after[field.Name] = value.DeepClone();
                }
            }
            else if (after[field.Name] is { } inner)
            {
                CarryInside(field, value, inner);
            }
            else if (field.FieldType == FieldTypeNames.Component && !field.Multiple && value is JsonObject nested && ComponentOf(field) is { } component)
            {
                JsonObject kept = [];
                CarryIntoObject(component, nested, kept);
                if (kept.Count > 0)
                {
                    after[field.Name] = kept;
                }
            }
        }
    }
}
