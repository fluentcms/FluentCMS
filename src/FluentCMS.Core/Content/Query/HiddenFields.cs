using System.Text.Json.Nodes;
using FluentCMS.Core.ProseMirror;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Schema;

namespace FluentCMS.Core.Content.Query;

// JSON stores a block or repeated component whole, and rich text the data of its inline blocks, and a hidden field stays
// out of every response. With dropUnknown, a key that is not a field of its block or component goes too, which is for
// values that did not come from storage. An inline block of a type the schema does not know has no fields to go by.
internal static class HiddenFields
{
    private const string Discriminator = "type";

    public static void Remove(SchemaModel schema, JsonNode? value, SchemaField field, bool dropUnknown = false) =>
        Remove(schema, value, field, dropUnknown, static inner => inner.Hidden);

    // What is restored from a version: what the system principal may hold, or what no client may write.
    public static void RemoveWhere(SchemaModel schema, JsonNode? value, SchemaField field, Func<SchemaField, bool> remove) =>
        Remove(schema, value, field, dropUnknown: true, remove);

    // Rich text outside JSON, at the top level or in a single component.
    public static RichText Remove(SchemaModel schema, RichText document, bool dropUnknown = false)
    {
        if (!InlineBlockJson.MayHold(document.Json) || InlineBlockJson.Parse(document.Json) is not { } node)
        {
            return document;
        }

        var before = node.DeepClone();
        RemoveInline(schema, node, dropUnknown, static inner => inner.Hidden);
        return JsonNode.DeepEquals(before, node) ? document : InlineBlockJson.ToRichText(node);
    }

    private static void Remove(SchemaModel schema, JsonNode? value, SchemaField field, bool dropUnknown, Func<SchemaField, bool> remove)
    {
        switch (field.FieldType)
        {
            case FieldTypeNames.RichText when value is JsonObject:
                RemoveInline(schema, value, dropUnknown, remove);
                break;
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

    private static void RemoveInline(SchemaModel schema, JsonNode document, bool dropUnknown, Func<SchemaField, bool> remove)
    {
        foreach (var block in InlineBlockJson.Find(document))
        {
            if (schema.FindBlock(block.BlockType) is { } known && block.Data is { } data)
            {
                Remove(schema, data, known, dropUnknown, remove);
            }
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
                if (key is not (Discriminator or BlockIds.Key) && type.FindField(key) is null)
                {
                    entry.Remove(key);
                }
            }
        }
    }
}
