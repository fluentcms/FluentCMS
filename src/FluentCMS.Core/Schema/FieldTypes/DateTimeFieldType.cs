using System.Globalization;
using System.Text.Json.Nodes;
using FluentCMS.Schema;

namespace FluentCMS.Core.Schema.FieldTypes;

// The value is kept as an instant in UTC, as the storage model keeps it; text without an offset is read as UTC.
internal sealed class DateTimeFieldType()
    : CoreFieldType(FieldTypeNames.DateTime, [typeof(DateTimeOffset), typeof(DateTimeOffset?)], FilterSets.Ordered, sortable: true, indexable: true)
{
    private static readonly string[] Formats =
    [
        "yyyy-MM-dd'T'HH:mm:ssK",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
        "yyyy-MM-dd'T'HH:mmK",
    ];

    public override bool TryCoerce(object? value, SchemaField field, out object? result, out string? message)
    {
        var raw = RawValue.Unwrap(value);
        switch (raw)
        {
            case null:
                return Ok(null, out result, out message);
            case DateTimeOffset moment:
                return Ok(moment.ToUniversalTime(), out result, out message);
            case DateTime moment:
                var utc = moment.Kind == DateTimeKind.Local ? moment.ToUniversalTime() : DateTime.SpecifyKind(moment, DateTimeKind.Utc);
                return Ok(new DateTimeOffset(utc), out result, out message);
            case string text:
                return DateTimeOffset.TryParseExact(text, Formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
                    ? Ok(parsed, out result, out message)
                    : Fail("is not an ISO 8601 date and time such as 2026-11-01T09:12:00Z", out result, out message);
            default:
                return Expected("a date and time", raw, out result, out message);
        }
    }

    public override JsonObject OpenApiSchema(SchemaField field) => new() { ["type"] = "string", ["format"] = "date-time" };
}
