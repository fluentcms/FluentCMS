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

    // What a client cannot see or change inside a component or a block stays when it sends the rest back. In a list, a
    // block keeps those values of the block at the same index, if that is of the same type. A single component only needs
    // this on a full update: a patch merged into it already keeps them, and may be the system principal clearing one.
    public void CarryHidden(SchemaType type, ContentDocument stored, ContentDocument composed, bool includingGroups)
    {
        foreach (var field in type.Fields)
        {
            stored.TryGetValue(field.Name, out var before);
            composed.TryGetValue(field.Name, out var after);
            if (field.FieldType == FieldTypeNames.Component && !field.Multiple)
            {
                if (includingGroups && before is ContentDocument oldGroup && after is ContentDocument newGroup
                    && schema.FindComponent((string)field.Settings[FieldSettings.Component]) is { } component)
                {
                    CarryIntoGroup(component, oldGroup, newGroup);
                }
            }
            else if (before is JsonNode oldNode && after is JsonNode newNode)
            {
                CarryInside(field, oldNode, newNode);
            }
        }
    }

    private void CarryIntoGroup(SchemaType component, ContentDocument before, ContentDocument after)
    {
        foreach (var field in component.Fields)
        {
            if ((field.Hidden || field.ReadOnly) && before.GetValueOrDefault(field.Name) is { } value && after.GetValueOrDefault(field.Name) is null)
            {
                after[field.Name] = value;
            }
            else if (before.GetValueOrDefault(field.Name) is JsonNode oldNode && after.GetValueOrDefault(field.Name) is JsonNode newNode)
            {
                CarryInside(field, oldNode, newNode);
            }
            else if (field.FieldType == FieldTypeNames.Component && !field.Multiple
                && before.GetValueOrDefault(field.Name) is ContentDocument oldGroup && after.GetValueOrDefault(field.Name) is ContentDocument newGroup
                && schema.FindComponent((string)field.Settings[FieldSettings.Component]) is { } inner)
            {
                CarryIntoGroup(inner, oldGroup, newGroup);
            }
        }
    }

    private void CarryInside(SchemaField field, JsonNode before, JsonNode after)
    {
        if (before is JsonArray oldItems && after is JsonArray newItems)
        {
            for (var index = 0; index < Math.Min(oldItems.Count, newItems.Count); index++)
            {
                if (oldItems[index] is JsonObject oldItem && newItems[index] is JsonObject newItem && OwnerOf(field, oldItem, newItem) is { } owner)
                {
                    CarryIntoObject(owner, oldItem, newItem);
                }
            }
        }
        else if (before is JsonObject oldObject && after is JsonObject newObject && OwnerOf(field, oldObject, newObject) is { } owner)
        {
            CarryIntoObject(owner, oldObject, newObject);
        }
    }

    private SchemaType? OwnerOf(SchemaField field, JsonObject before, JsonObject after)
    {
        if (field.FieldType == FieldTypeNames.Component)
        {
            return schema.FindComponent((string)field.Settings[FieldSettings.Component]);
        }

        return field.FieldType == FieldTypeNames.Blocks
            && before[Discriminator] is JsonValue oldName && after[Discriminator] is JsonValue newName
            && oldName.TryGetValue<string>(out var name) && newName.TryGetValue<string>(out var same) && name == same
                ? schema.FindBlock(name)
                : null;
    }

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
                after[field.Name] ??= value.DeepClone();
            }
            else if (after[field.Name] is { } inner)
            {
                CarryInside(field, value, inner);
            }
        }
    }
}
