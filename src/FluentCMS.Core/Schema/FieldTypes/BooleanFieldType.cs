using System.Text.Json.Nodes;
using FluentCMS.Schema;

namespace FluentCMS.Core.Schema.FieldTypes;

internal sealed class BooleanFieldType()
    : CoreFieldType(FieldTypeNames.Boolean, [typeof(bool), typeof(bool?)], FilterSets.Equality, sortable: true, indexable: true)
{
    public override bool TryCoerce(object? value, SchemaField field, out object? result, out string? message)
    {
        var raw = RawValue.Unwrap(value);
        switch (raw)
        {
            case null:
                return Ok(null, out result, out message);
            case bool flag:
                return Ok(flag, out result, out message);
            case string text when text.Equals("true", StringComparison.OrdinalIgnoreCase):
                return Ok(true, out result, out message);
            case string text when text.Equals("false", StringComparison.OrdinalIgnoreCase):
                return Ok(false, out result, out message);
            case string:
                return Fail("is not true or false", out result, out message);
            default:
                return Expected("a boolean", raw, out result, out message);
        }
    }

    public override JsonObject OpenApiSchema(SchemaField field) => new() { ["type"] = "boolean" };
}
