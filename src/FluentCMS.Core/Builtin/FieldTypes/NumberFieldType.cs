using System.Text.Json;
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
}
