using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Builtin.FieldTypes;

// Any JSON value, for structures the other types do not describe.
internal sealed class JsonFieldType : IFieldType
{
    public string Name => "json";

    public IReadOnlyList<Error> ValidateSettings(JsonElement settings, FieldTypeRegistry fieldTypes)
    {
        return new FieldSettingsReader(Name, settings).Errors();
    }

    public IReadOnlyList<Error> ValidateValue(JsonElement value, JsonElement settings, FieldTypeRegistry fieldTypes)
    {
        return [];
    }

    public FieldValueDescription DescribeValue(JsonElement settings, FieldTypeRegistry fieldTypes)
    {
        return new FieldValueDescription(new JsonObject(), new JsonObject { ["key"] = "value" }, "Any JSON value.");
    }
}
