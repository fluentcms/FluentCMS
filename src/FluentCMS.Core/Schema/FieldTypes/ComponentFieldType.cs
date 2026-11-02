using System.Text.Json.Nodes;
using FluentCMS.Schema;

namespace FluentCMS.Core.Schema.FieldTypes;

// A single component is flattened into columns of its own, a repeatable one is JSON. The value coerces to a
// JsonObject, or a JsonArray of them when repeatable. The fields inside it are checked with the component's own
// schema by the content service, which has the schema model; a descriptor only sees one field.
internal sealed class ComponentFieldType()
    : CoreFieldType(
        FieldTypeNames.Component,
        [typeof(object)],
        [],
        sortable: false,
        indexable: false,
        new FieldSettingDescriptor { Key = FieldSettings.Component, Kind = FieldSettingKind.String, Required = true })
{
    public override FieldStorageKind StorageKind(SchemaField field) => field.Multiple ? FieldStorageKind.Json : FieldStorageKind.Column;

    public override bool TryCoerce(object? value, SchemaField field, out object? result, out string? message)
    {
        if (!RawValue.TryReadJson(value, out var node, out var problem))
        {
            return Fail(problem!, out result, out message);
        }

        switch (node)
        {
            case null:
                return Ok(null, out result, out message);
            case JsonObject when !field.Multiple:
                return Ok(node, out result, out message);
            case JsonArray items when field.Multiple && items.All(item => item is JsonObject):
                return Ok(node, out result, out message);
            default:
                return Fail(field.Multiple ? "is not a list of objects" : "is not an object", out result, out message);
        }
    }

    public override JsonObject OpenApiSchema(SchemaField field)
    {
        var reference = new JsonObject { ["$ref"] = OpenApiNames.ComponentRef(SettingValues.Text(field, FieldSettings.Component) ?? "") };
        return field.Multiple ? new JsonObject { ["type"] = "array", ["items"] = reference } : reference;
    }
}
