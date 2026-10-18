using System.Text.Json;
using System.Text.Json.Nodes;
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

    public IReadOnlyList<Error> ValidateValue(JsonElement value, JsonElement settings, FieldTypeRegistry fieldTypes)
    {
        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        if (new FieldSettingsReader(Name, settings).Flag("dateOnly"))
        {
            return IsoDateTimes.IsDate(text) ? [] : Values.Invalid("The value must be a calendar date as YYYY-MM-DD, such as 2026-01-31.");
        }

        return IsoDateTimes.IsPointInTime(text)
            ? []
            : Values.Invalid("The value must be a point in time in ISO 8601 with an offset, such as 2026-01-31T09:30:00Z.");
    }

    public FieldValueDescription DescribeValue(JsonElement settings, FieldTypeRegistry fieldTypes)
    {
        return new FieldSettingsReader(Name, settings).Flag("dateOnly")
            ? new FieldValueDescription(
                new JsonObject { ["type"] = "string", ["format"] = "date" },
                "2026-01-31",
                "A calendar date as YYYY-MM-DD.")
            : new FieldValueDescription(
                new JsonObject { ["type"] = "string", ["format"] = "date-time" },
                "2026-01-31T09:30:00Z",
                "A point in time in ISO 8601 with an offset, such as 2026-01-31T09:30:00Z.");
    }
}
