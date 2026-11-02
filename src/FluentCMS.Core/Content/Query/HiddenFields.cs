using System.Text.Json.Nodes;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Schema;

namespace FluentCMS.Core.Content.Query;

// JSON stores a block or repeated component whole, and a hidden field stays out of every response. With dropUnknown,
// a key that is not a field of its block or component goes too, which is for values that did not come from storage.
internal static class HiddenFields
{
    private const string Discriminator = "type";

    public static void Remove(SchemaModel schema, JsonNode? value, SchemaField field, bool dropUnknown = false)
    {
        switch (field.FieldType)
        {
            case FieldTypeNames.Blocks when value is JsonArray blocks:
                foreach (var block in blocks.OfType<JsonObject>())
                {
                    if (block[Discriminator] is JsonValue name && name.TryGetValue<string>(out var blockName) && schema.FindBlock(blockName) is { } type)
                    {
                        Remove(schema, block, type, dropUnknown);
                    }
                    else if (dropUnknown)
                    {
                        block.Clear();
                    }
                }

                break;
            case FieldTypeNames.Component when schema.FindComponent((string)field.Settings[FieldSettings.Component]) is { } component:
                foreach (var item in value is JsonArray items ? items.OfType<JsonObject>() : value is JsonObject single ? [single] : [])
                {
                    Remove(schema, item, component, dropUnknown);
                }

                break;
        }
    }

    private static void Remove(SchemaModel schema, JsonObject entry, SchemaType type, bool dropUnknown)
    {
        foreach (var field in type.Fields)
        {
            if (field.Hidden)
            {
                entry.Remove(field.Name);
            }
            else if (entry[field.Name] is { } inner)
            {
                Remove(schema, inner, field, dropUnknown);
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
