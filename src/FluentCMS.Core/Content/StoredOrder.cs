using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.ProseMirror;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Core.Storage;
using FluentCMS.Schema;

namespace FluentCMS.Core.Content;

// A provider may give JSON back in an order and spacing of its own, as jsonb does, so a read puts a value back in the form
// EntryCanonicalizer writes it: a block's type and id first and then its fields as the schema lists them, a reference as its
// id and type, and rich text as RichTextSerializer writes it. A key the schema does not know keeps its place after those, and
// a json field, which has no fields to go by, keeps the order it was read in.
internal static class StoredOrder
{
    private const string Discriminator = "type";

    // A document that does not parse is returned as read, for its own rules to refuse.
    public static RichText Of(SchemaModel schema, RichText document)
    {
        try
        {
            var ordered = document;
            if (InlineBlockJson.MayHold(document.Json) && InlineBlockJson.Parse(document.Json) is { } root)
            {
                OrderInline(schema, root);
                ordered = new RichText(InlineBlockJson.Text(root));
            }

            return RichTextParser.TryParse(ordered, out var parsed, out _) ? RichTextSerializer.ToRichText(parsed) : document;
        }
        catch (JsonException)
        {
            return document;
        }
    }

    public static JsonNode? Of(SchemaModel schema, SchemaField field, JsonNode? value)
    {
        if (schema.FieldTypes.Find(field.FieldType)?.StorageKind(field) == FieldStorageKind.Reference)
        {
            foreach (var stub in Items(value))
            {
                Order(stub, [BlockIds.Key, Discriminator], []);
            }

            return value;
        }

        switch (field.FieldType)
        {
            case FieldTypeNames.RichText when value is JsonObject:
                return JsonDepth.Parse(Of(schema, new RichText(InlineBlockJson.Text(value))).Json);
            case FieldTypeNames.Blocks:
                foreach (var block in Items(value))
                {
                    if (block[Discriminator] is JsonValue name && name.TryGetValue<string>(out var blockName) && schema.FindBlock(blockName) is { } known)
                    {
                        Order(schema, block, known, [Discriminator, BlockIds.Key]);
                    }
                }

                return value;
            case FieldTypeNames.Component when field.Settings.TryGetValue(FieldSettings.Component, out var name) && schema.FindComponent((string)name) is { } component:
                foreach (var item in Items(value))
                {
                    Order(schema, item, component, []);
                }

                return value;
            default:
                return value;
        }
    }

    private static void OrderInline(SchemaModel schema, JsonNode document)
    {
        foreach (var inline in InlineBlockJson.Find(document))
        {
            if (schema.FindBlock(inline.BlockType) is { } block && inline.Data is { } data)
            {
                Order(schema, data, block, []);
            }
        }
    }

    private static void Order(SchemaModel schema, JsonObject item, SchemaType owner, string[] first)
    {
        foreach (var field in owner.Fields)
        {
            if (item[field.Name] is not { } inner)
            {
                continue;
            }

            var ordered = Of(schema, field, inner);
            if (!ReferenceEquals(ordered, inner))
            {
                item[field.Name] = ordered;
            }
        }

        Order(item, first, owner.Fields.Select(field => field.Name));
    }

    private static void Order(JsonObject item, IEnumerable<string> first, IEnumerable<string> then)
    {
        var pairs = item.ToList();
        item.Clear();
        foreach (var key in first.Concat(then))
        {
            var at = pairs.FindIndex(pair => pair.Key == key);
            if (at >= 0)
            {
                item[key] = pairs[at].Value;
                pairs.RemoveAt(at);
            }
        }

        foreach (var (key, value) in pairs)
        {
            item[key] = value;
        }
    }

    private static List<JsonObject> Items(JsonNode? value) =>
        value is JsonArray items ? items.OfType<JsonObject>().ToList() : value is JsonObject single ? [single] : [];
}
