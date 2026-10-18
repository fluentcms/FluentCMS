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
