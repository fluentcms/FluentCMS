using System.Text.Json.Nodes;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Schema;

namespace FluentCMS.Core.Content.Query;

// JSON stores a block or repeated component whole, and a hidden field stays out of every response. With dropUnknown,
// a key that is not a field of its block or component goes too, which is for values that did not come from storage.
internal static class HiddenFields
{
    private const string Discriminator = "type";

    public static void Remove(SchemaModel schema, JsonNode? value, SchemaField field, bool dropUnknown = false) =>
        Remove(schema, value, field, dropUnknown, static inner => inner.Hidden);

    // What is restored from a version: what the system principal may hold, or what no client may write.
    public static void RemoveWhere(SchemaModel schema, JsonNode? value, SchemaField field, Func<SchemaField, bool> remove) =>
        Remove(schema, value, field, dropUnknown: true, remove);

    private static void Remove(SchemaModel schema, JsonNode? value, SchemaField field, bool dropUnknown, Func<SchemaField, bool> remove)
    {
        switch (field.FieldType)
        {
            case FieldTypeNames.Blocks when value is JsonArray blocks:
                foreach (var block in blocks.OfType<JsonObject>().ToList())
                {
                    var known = block[Discriminator] is JsonValue name && name.TryGetValue<string>(out var blockName) ? schema.FindBlock(blockName) : null;
                    if (dropUnknown && (known is null || !SettingValues.List(field, FieldSettings.Blocks).Contains(known.Name)))
                    {
                        blocks.Remove(block);
                    }
                    else if (known is not null)
                    {
                        Remove(schema, block, known, dropUnknown, remove);
                    }
                }

                break;
            case FieldTypeNames.Component when schema.FindComponent((string)field.Settings[FieldSettings.Component]) is { } component:
                foreach (var item in value is JsonArray items ? items.OfType<JsonObject>() : value is JsonObject single ? [single] : [])
                {
                    Remove(schema, item, component, dropUnknown, remove);
                }

                break;
        }
    }

    private static void Remove(SchemaModel schema, JsonObject entry, SchemaType type, bool dropUnknown, Func<SchemaField, bool> remove)
    {
        foreach (var field in type.Fields)
        {
            if (remove(field))
            {
                entry.Remove(field.Name);
            }
            else if (entry[field.Name] is { } inner)
            {
                Remove(schema, inner, field, dropUnknown, remove);
            }
        }

        if (dropUnknown)
        {
            foreach (var key in entry.Select(pair => pair.Key).ToList())
            {
                if (key != Discriminator && type.FindField(key) is null)
                {
                    entry.Remove(key);
                }
            }
        }
    }
}
