using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Schema;

namespace FluentCMS.Core.Schema.FieldTypes;

// The value is a JsonNode. Text is read as JSON text, so the string "x" is not valid but "\"x\"" is; a JsonElement is taken as it is.
internal sealed class JsonFieldType()
    : CoreFieldType(
        FieldTypeNames.Json,
        [typeof(JsonElement), typeof(JsonNode)],
        [FilterOperator.Equal, FilterOperator.NotEqual, FilterOperator.Contains, .. FilterSets.NullChecks],
        sortable: false,
        indexable: true)
{
    public override FieldStorageKind StorageKind(SchemaField field) => FieldStorageKind.Json;

    public override bool TryCoerce(object? value, SchemaField field, out object? result, out string? message)
    {
        if (value is bool or string or JsonElement or JsonNode or null || !RawValue.IsNumber(value))
        {
            return RawValue.TryReadJson(value switch { bool flag => JsonValue.Create(flag), _ => value }, out var node, out var problem)
                ? Ok(node, out result, out message)
                : Fail(problem!, out result, out message);
        }

        return RawValue.TryToDecimal(value, out var number)
            ? Ok(JsonValue.Create(number), out result, out message)
            : Fail("is not a number that JSON can hold", out result, out message);
    }

    public override JsonObject OpenApiSchema(SchemaField field) => [];
}
