using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Builtin.FieldTypes;

// Formatted text, stored as Markdown so every entry has a Markdown representation (SCH-02, API-03).
internal sealed class RichTextFieldType : IFieldType
{
    public string Name => "richText";

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
        var schema = new JsonObject { ["type"] = "string", ["contentMediaType"] = "text/markdown" };
        if (maxLength is { } limit)
        {
            schema["maxLength"] = limit;
        }

        return new FieldValueDescription(
            schema,
            Examples.Fit("Some **Markdown** text.", maxLength),
            $"Formatted text written as Markdown{Examples.AtMost(maxLength)}.");
    }
}
