using System.Globalization;
using System.Text.Json.Nodes;
using FluentCMS.Schema;

namespace FluentCMS.Core.Schema.FieldTypes;

internal sealed class IntegerFieldType()
    : CoreFieldType(FieldTypeNames.Integer, [typeof(int), typeof(int?), typeof(long), typeof(long?)], FilterSets.Ordered, sortable: true, indexable: true)
{
    public override bool TryCoerce(object? value, SchemaField field, out object? result, out string? message)
    {
        var raw = RawValue.Unwrap(value);
        switch (raw)
        {
            case null:
                return Ok(null, out result, out message);
            case string text:
                return long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed)
                    ? Ok(parsed, out result, out message)
                    : Fail("is not a whole number", out result, out message);
            case not null when raw is not bool && RawValue.IsNumber(raw):
                return RawValue.TryToDecimal(raw, out var number) && number == decimal.Truncate(number) && number >= long.MinValue && number <= long.MaxValue
                    ? Ok((long)number, out result, out message)
                    : Fail("is not a whole number that fits in 64 bits", out result, out message);
            default:
                return Expected("a whole number", raw, out result, out message);
        }
    }

    public override JsonObject OpenApiSchema(SchemaField field)
    {
        var schema = new JsonObject { ["type"] = "integer", ["format"] = "int64" };
        if (field.Minimum is { } min)
        {
            schema["minimum"] = min;
        }

        if (field.Maximum is { } max)
        {
            schema["maximum"] = max;
        }

        return schema;
    }
}
