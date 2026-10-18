using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Builtin.FieldTypes;

internal sealed class NumberFieldType : IFieldType
{
    public string Name => "number";

    public IReadOnlyList<Error> ValidateSettings(JsonElement settings, FieldTypeRegistry fieldTypes)
    {
        var reader = new FieldSettingsReader(Name, settings);
        var integer = reader.Flag("integer");
        var min = reader.Number("min");
        var max = reader.Number("max");

        if (integer && min is { } low && low != decimal.Truncate(low))
        {
            reader.Invalid("min", "must be a whole number when 'integer' is true");
        }

        if (integer && max is { } high && high != decimal.Truncate(high))
        {
            reader.Invalid("max", "must be a whole number when 'integer' is true");
        }

        if (min > max)
        {
            reader.Invalid("max", "must not be less than 'min'");
        }

        return reader.Errors();
    }

    public FieldValueDescription DescribeValue(JsonElement settings, FieldTypeRegistry fieldTypes)
    {
        var reader = new FieldSettingsReader(Name, settings);
        var integer = reader.Flag("integer");
        var min = reader.Number("min");
        var max = reader.Number("max");

        var schema = new JsonObject { ["type"] = integer ? "integer" : "number" };
        if (min is { } low)
        {
            schema["minimum"] = low;
        }

        if (max is { } high)
        {
            schema["maximum"] = high;
        }

        var range = (min, max) switch
        {
            (not null, not null) => string.Create(CultureInfo.InvariantCulture, $" from {min} to {max}"),
            (not null, null) => string.Create(CultureInfo.InvariantCulture, $" of at least {min}"),
            (null, not null) => string.Create(CultureInfo.InvariantCulture, $" of at most {max}"),
            _ => string.Empty,
        };

        return new FieldValueDescription(schema, Example(integer, min ?? max ?? 0m), $"{(integer ? "A whole number" : "A number")}{range}.");
    }

    // A whole number is written without the ".0" a setting may carry, so the example reads as an integer.
    private static JsonNode Example(bool integer, decimal value)
    {
        if (!integer)
        {
            return value;
        }

        var whole = decimal.Truncate(value);
        return whole is >= long.MinValue and <= long.MaxValue ? decimal.ToInt64(whole) : whole;
    }
}
