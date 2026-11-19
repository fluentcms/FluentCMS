using System.Security.Cryptography;
using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;

namespace FluentCMS.Admin.Fields;

// Components and blocks in a form (spec 4.8): their types in the schema, how deep a block type nests, the ids of blocks
// (ADR 0002) and the error paths inside them.
internal static class NestedValues
{
    // Decision 20, as the server's SchemaValidator counts it: the components and blocks between a type and its deepest field.
    public const int MaxDepth = 3;

    // The name of the cascading value that holds how many blocks and components hold a form's fields.
    public const string Nesting = "FluentCMS.Admin.Nesting";

    private const string IdKey = "id";
    private const string TypeKey = "type";
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
    private const int IdLength = 12;

    // 64 characters, so that each random byte picks one evenly.
    public static string NewId() => string.Create(IdLength, RandomNumberGenerator.GetBytes(IdLength), static (id, bytes) =>
    {
        for (var index = 0; index < id.Length; index++)
        {
            id[index] = Alphabet[bytes[index] & 63];
        }
    });

    public static string? Text(JsonNode? node, string key) =>
        node is JsonObject item && item[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    public static string? IdOf(JsonNode? block) => Text(block, IdKey);

    public static string? TypeOf(JsonNode? block) => Text(block, TypeKey);

    public static SchemaTypeDescription? ComponentOf(SchemaDescription schema, SchemaFieldDescription field) =>
        Text(field.Settings, "component") is { } name ? schema.Components.FirstOrDefault(type => type.Name == name) : null;

    // The block types of the field's union that the schema has, in the union's order.
    public static IReadOnlyList<SchemaTypeDescription> BlocksOf(SchemaDescription schema, SchemaFieldDescription field) =>
        field.Settings["blocks"] is JsonArray names
            ? [.. names.Select(name => name is JsonValue value && value.TryGetValue<string>(out var text) ? schema.Blocks.FirstOrDefault(type => type.Name == text) : null).OfType<SchemaTypeDescription>()]
            : [];

    // A type that nests itself has no depth, so it never fits; the schema refuses such a type anyway.
    public static int Depth(SchemaDescription schema, SchemaTypeDescription type) => Depth(schema, type, []);

    // A new block: its type, a fresh id, and the defaults of its fields.
    public static JsonObject NewBlock(SchemaTypeDescription type)
    {
        var block = new JsonObject { [TypeKey] = type.Name, [IdKey] = NewId() };
        foreach (var (name, value) in NewComponent(type))
        {
            block[name] = value?.DeepClone();
        }

        return block;
    }

    public static JsonObject NewComponent(SchemaTypeDescription type)
    {
        var values = new JsonObject();
        foreach (var field in type.Fields.Where(field => !field.Hidden && !field.ReadOnly && field.DefaultValue is not null))
        {
            values[field.Name] = field.DefaultValue!.DeepClone();
        }

        return values;
    }

    // A copy is a new block, so it and every block and inline block it holds take fresh ids: an id repeated in a field is
    // refused (FCMS1407), and an id the server does not hold is a new block.
    public static JsonObject Copy(SchemaDescription schema, SchemaTypeDescription type, JsonObject block)
    {
        var copy = block.DeepClone().AsObject();
        if (copy.ContainsKey(IdKey))
        {
            copy[IdKey] = NewId();
        }
        else
        {
            copy.Insert(Math.Min(1, copy.Count), IdKey, NewId());
        }

        RenewIds(schema, type, copy, 1);
        return copy;
    }

    // Whether an error's path is at or under the path: sections[0] holds sections[0], sections[0].text and sections[0].items[1].
    public static bool Under(string? path, string prefix) =>
        path is not null && path.StartsWith(prefix, StringComparison.Ordinal)
        && (path.Length == prefix.Length || path[prefix.Length] is '.' or '[');

    private static int Depth(SchemaDescription schema, SchemaTypeDescription type, HashSet<string> visiting)
    {
        if (!visiting.Add(type.Name))
        {
            return int.MaxValue / 2;
        }

        var deepest = 0;
        foreach (var field in type.Fields)
        {
            IEnumerable<SchemaTypeDescription> children = field.Type switch
            {
                "component" => ComponentOf(schema, field) is { } component ? [component] : [],
                "blocks" => BlocksOf(schema, field),
                _ => [],
            };
            foreach (var child in children)
            {
                deepest = Math.Max(deepest, Depth(schema, child, visiting));
            }
        }

        visiting.Remove(type.Name);
        return deepest + 1;
    }

    private static void RenewIds(SchemaDescription schema, SchemaTypeDescription type, JsonObject values, int depth)
    {
        if (depth > MaxDepth)
        {
            return;
        }

        foreach (var field in type.Fields)
        {
            switch (field.Type, values[field.Name])
            {
                case ("blocks", JsonArray blocks):
                    foreach (var block in blocks.OfType<JsonObject>())
                    {
                        if (block.ContainsKey(IdKey))
                        {
                            block[IdKey] = NewId();
                        }

                        if (schema.Blocks.FirstOrDefault(item => item.Name == TypeOf(block)) is { } blockType)
                        {
                            RenewIds(schema, blockType, block, depth + 1);
                        }
                    }

                    break;
                case ("component", JsonNode component) when ComponentOf(schema, field) is { } componentType:
                    foreach (var item in component is JsonArray items ? items.OfType<JsonObject>() : component is JsonObject one ? [one] : [])
                    {
                        RenewIds(schema, componentType, item, depth + 1);
                    }

                    break;
                case ("richText", JsonObject document):
                    RenewInlineIds(schema, document, depth);
                    break;
            }
        }
    }

    private static void RenewInlineIds(SchemaDescription schema, JsonObject node, int depth)
    {
        if (Text(node, TypeKey) == "inlineBlock" && node["attrs"] is JsonObject attributes)
        {
            if (attributes.ContainsKey(IdKey))
            {
                attributes[IdKey] = NewId();
            }

            if (attributes["data"] is JsonObject data && schema.Blocks.FirstOrDefault(item => item.Name == Text(attributes, "blockType")) is { } blockType)
            {
                RenewIds(schema, blockType, data, depth + 1);
            }
        }

        if (node["content"] is JsonArray content)
        {
            foreach (var child in content.OfType<JsonObject>())
            {
                RenewInlineIds(schema, child, depth);
            }
        }
    }
}
