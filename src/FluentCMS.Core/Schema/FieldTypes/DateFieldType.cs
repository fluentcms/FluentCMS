using System.Globalization;
using System.Text.Json.Nodes;
using FluentCMS.Schema;

namespace FluentCMS.Core.Schema.FieldTypes;

internal sealed class DateFieldType()
    : CoreFieldType(FieldTypeNames.Date, [typeof(DateOnly), typeof(DateOnly?)], FilterSets.Ordered, sortable: true, indexable: true)
{
    private const string Format = "yyyy-MM-dd";

    public override bool TryCoerce(object? value, SchemaField field, out object? result, out string? message)
    {
        var raw = RawValue.Unwrap(value);
        switch (raw)
        {
            case null:
                return Ok(null, out result, out message);
            case DateOnly day:
                return Ok(day, out result, out message);
            case DateTime moment when moment.TimeOfDay == TimeSpan.Zero:
                return Ok(DateOnly.FromDateTime(moment), out result, out message);
            case DateTime:
                return Fail("has a time of day, but the field holds a date only", out result, out message);
            case string text:
                return DateOnly.TryParseExact(text, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                    ? Ok(parsed, out result, out message)
                    : Fail($"is not a date in the form {Format}", out result, out message);
            default:
                return Expected("a date", raw, out result, out message);
        }
    }

    public override JsonObject OpenApiSchema(SchemaField field) => new() { ["type"] = "string", ["format"] = "date" };
}
