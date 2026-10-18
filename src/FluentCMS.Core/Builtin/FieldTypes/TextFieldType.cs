using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Builtin.FieldTypes;

// Plain text on one or more lines.
internal sealed class TextFieldType : IFieldType
{
    public string Name => "text";

    public IReadOnlyList<Error> ValidateSettings(JsonElement settings, FieldTypeRegistry fieldTypes)
    {
        var reader = new FieldSettingsReader(Name, settings);
        reader.PositiveInteger("maxLength");

        return reader.Errors();
    }

    public IReadOnlyList<Error> ValidateValue(JsonElement value, JsonElement settings, FieldTypeRegistry fieldTypes)
    {
        return Values.Text(value, new FieldSettingsReader(Name, settings).PositiveInteger("maxLength"));
    }

    public FieldValueDescription DescribeValue(JsonElement settings, FieldTypeRegistry fieldTypes)
    {
        var maxLength = new FieldSettingsReader(Name, settings).PositiveInteger("maxLength");
        var schema = new JsonObject { ["type"] = "string" };
        if (maxLength is { } limit)
        {
            schema["maxLength"] = limit;
        }

        return new FieldValueDescription(schema, Examples.Fit("Example text", maxLength), $"Plain text{Examples.AtMost(maxLength)}.");
    }
}
