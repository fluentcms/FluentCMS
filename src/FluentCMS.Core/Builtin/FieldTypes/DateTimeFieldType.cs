using System.Text.Json;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Builtin.FieldTypes;

// A point in time, or a calendar date when 'dateOnly' is true.
internal sealed class DateTimeFieldType : IFieldType
{
    public string Name => "dateTime";

    public IReadOnlyList<Error> ValidateSettings(JsonElement settings, FieldTypeRegistry fieldTypes)
    {
        var reader = new FieldSettingsReader(Name, settings);
        reader.Flag("dateOnly");

        return reader.Errors();
    }
}
