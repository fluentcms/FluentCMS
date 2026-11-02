using System.Globalization;
using System.Text.Json.Nodes;
using FluentCMS.Schema;

namespace FluentCMS.Core.Schema.FieldTypes;

internal sealed class DecimalFieldType()
    : CoreFieldType(
        FieldTypeNames.Decimal,
        [typeof(decimal), typeof(decimal?)],
        FilterSets.Ordered,
        sortable: true,
        indexable: true,
        new FieldSettingDescriptor(FieldSettings.Precision, FieldSettingKind.Integer),
        new FieldSettingDescriptor(FieldSettings.Scale, FieldSettingKind.Integer))
{
    private const int MaxScale = 28;

    public override bool TryCoerce(object? value, SchemaField field, out object? result, out string? message)
    {
        var raw = RawValue.Unwrap(value);
        decimal number;
        switch (raw)
        {
            case null:
                return Ok(null, out result, out message);
            case string text:
                if (!decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out number))
                {
                    return Fail("is not a number", out result, out message);
                }

                break;
            case not null when raw is not bool && RawValue.IsNumber(raw):
                if (!RawValue.TryToDecimal(raw, out number))
                {
                    return Fail("is not a number that fits in a decimal", out result, out message);
                }

                break;
            default:
                return Expected("a number", raw, out result, out message);
        }

        number = SchemaValue.NormalizeDecimal(number);
        return Fits(number, field) is { } problem ? Fail(problem, out result, out message) : Ok(number, out result, out message);
    }

    public override JsonObject OpenApiSchema(SchemaField field)
    {
        var schema = new JsonObject { ["type"] = "number" };
        if (SettingValues.Int(field, FieldSettings.Scale) is > 0 and <= MaxScale and var scale)
        {
            schema["multipleOf"] = new decimal(1, 0, 0, false, (byte)scale);
        }

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

    // Precision counts all significant digits and scale those after the decimal point. A precision without a scale
    // limits only the total number of digits.
    private static string? Fits(decimal number, SchemaField field)
    {
        var scale = SettingValues.Int(field, FieldSettings.Scale);
        var precision = SettingValues.Int(field, FieldSettings.Precision);

        var fraction = (decimal.GetBits(number)[3] >> 16) & 0xFF;
        var whole = decimal.Truncate(Math.Abs(number));
        var wholeDigits = whole == 0 ? 0 : whole.ToString(CultureInfo.InvariantCulture).Length;

        if (scale is { } places && fraction > places)
        {
            return $"has {fraction} decimal places, more than the {places} the field allows";
        }

        if (precision is { } digits)
        {
            var allowedWholeDigits = digits - (scale ?? fraction);
            if (wholeDigits > allowedWholeDigits)
            {
                return $"has more than the {digits} significant digits the field allows";
            }
        }

        return null;
    }
}
