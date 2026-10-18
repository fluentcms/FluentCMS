using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Builtin.FieldTypes;

// One value out of a fixed list; a list of enum holds several.
internal sealed class EnumFieldType : IFieldType
{
    public string Name => "enum";

    public IReadOnlyList<Error> ValidateSettings(JsonElement settings, FieldTypeRegistry fieldTypes)
    {
        var reader = new FieldSettingsReader(Name, settings);
        reader.RequiredStringList("values");

        return reader.Errors();
    }

    public IReadOnlyList<Error> ValidateValue(JsonElement value, JsonElement settings, FieldTypeRegistry fieldTypes)
    {
        var values = new FieldSettingsReader(Name, settings).RequiredStringList("values") ?? [];

        return value.ValueKind == JsonValueKind.String && values.Contains(value.GetString()!, StringComparer.Ordinal)
            ? []
            : Values.Invalid($"The value must be one of: {string.Join(", ", values)}.");
    }

    public FieldValueDescription DescribeValue(JsonElement settings, FieldTypeRegistry fieldTypes)
    {
        var values = new FieldSettingsReader(Name, settings).RequiredStringList("values") ?? [];

        return new FieldValueDescription(
            new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. values.Select(value => (JsonNode?)value)]) },
            values.FirstOrDefault(),
            $"One of: {string.Join(", ", values)}.");
    }
}
