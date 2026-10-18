using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Builtin.FieldTypes;

internal sealed class BooleanFieldType : IFieldType
{
    public string Name => "boolean";

    public IReadOnlyList<Error> ValidateSettings(JsonElement settings, FieldTypeRegistry fieldTypes)
    {
        return new FieldSettingsReader(Name, settings).Errors();
    }

    public IReadOnlyList<Error> ValidateValue(JsonElement value, JsonElement settings, FieldTypeRegistry fieldTypes)
    {
        return value.ValueKind is JsonValueKind.True or JsonValueKind.False ? [] : Values.Invalid("The value must be true or false.");
    }

    public FieldValueDescription DescribeValue(JsonElement settings, FieldTypeRegistry fieldTypes)
    {
        return new FieldValueDescription(new JsonObject { ["type"] = "boolean" }, true, "true or false.");
    }
}
