using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Schema;

namespace FluentCMS.Core.Schema.FieldTypes;

// The value coerces to a JsonArray of objects that each name their block in "type", or stays a BlockList. Whether
// the block is one the field accepts, and the fields inside it, are checked by the content service.
internal sealed class BlocksFieldType()
    : CoreFieldType(
        FieldTypeNames.Blocks,
        [typeof(BlockList)],
        [FilterOperator.HasBlock, .. FilterSets.NullChecks],
        sortable: false,
        indexable: false,
        new FieldSettingDescriptor(FieldSettings.Blocks, FieldSettingKind.StringList, Required: true))
{
    private const string Discriminator = "type";

    public override FieldStorageKind StorageKind(SchemaField field) => FieldStorageKind.Json;

    public override bool TryCoerce(object? value, SchemaField field, out object? result, out string? message)
    {
        if (value is BlockList)
        {
            return Ok(value, out result, out message);
        }

        if (!RawValue.TryReadJson(value, out var node, out var problem))
        {
            return Fail(problem!, out result, out message);
        }

        switch (node)
        {
            case null:
                return Ok(null, out result, out message);
            case JsonArray blocks:
                for (var index = 0; index < blocks.Count; index++)
                {
                    if (blocks[index] is not JsonObject block || block[Discriminator] is not JsonValue name || !name.TryGetValue<string>(out var type) || type.Length == 0)
                    {
                        return Fail($"block {index} is not an object with a \"{Discriminator}\" naming its block", out result, out message);
                    }
                }

                return Ok(node, out result, out message);
            default:
                return Fail("is not a list of blocks", out result, out message);
        }
    }

    public override JsonObject OpenApiSchema(SchemaField field)
    {
        var names = SettingValues.List(field, FieldSettings.Blocks);
        JsonObject mapping = [];
        foreach (var name in names)
        {
            mapping[name] = OpenApiNames.BlockRef(name);
        }

        return new JsonObject
        {
            ["type"] = "array",
            ["items"] = new JsonObject
            {
                ["oneOf"] = new JsonArray([.. names.Select(name => (JsonNode)new JsonObject { ["$ref"] = OpenApiNames.BlockRef(name) })]),
                ["discriminator"] = new JsonObject { ["propertyName"] = Discriminator, ["mapping"] = mapping },
            },
        };
    }
}
