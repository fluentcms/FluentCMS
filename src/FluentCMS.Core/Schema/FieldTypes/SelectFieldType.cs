using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Schema;

namespace FluentCMS.Core.Schema.FieldTypes;

// Whether a value is one of the options is a validation rule, not a coercion rule.
internal sealed class SelectFieldType()
    : CoreFieldType(
        FieldTypeNames.Select,
        [typeof(string), typeof(Enum)],
        [FilterOperator.Equal, FilterOperator.NotEqual, FilterOperator.In, FilterOperator.NotIn, FilterOperator.Contains, .. FilterSets.NullChecks],
        sortable: true,
        indexable: true,
        new FieldSettingDescriptor { Key = FieldSettings.Options, Kind = FieldSettingKind.StringList, Required = true })
{
    public override bool SupportsMultiple => true;

    public override FieldStorageKind StorageKind(SchemaField field) => field.Multiple ? FieldStorageKind.Json : FieldStorageKind.Column;

    public override bool Indexable(SchemaField field) => !field.Multiple;

    public override bool TryCoerce(object? value, SchemaField field, out object? result, out string? message)
    {
        var raw = RawValue.Unwrap(value);
        if (raw is null)
        {
            return Ok(null, out result, out message);
        }

        if (!field.Multiple)
        {
            return TryReadOption(raw, out var option)
                ? Ok(option, out result, out message)
                : Expected("a string", raw, out result, out message);
        }

        IEnumerable<object?> items;
        switch (raw)
        {
            case JsonElement { ValueKind: JsonValueKind.Array } array:
                items = array.EnumerateArray().Select(item => RawValue.Unwrap(item));
                break;
            case string or JsonElement:
                return Expected("a list of strings", raw, out result, out message);
            case IEnumerable list:
                items = list.Cast<object?>();
                break;
            default:
                return Expected("a list of strings", raw, out result, out message);
        }

        List<string> options = [];
        foreach (var item in items)
        {
            if (!TryReadOption(item, out var option))
            {
                return Fail($"item {options.Count} is not a string: it is {RawValue.Describe(item)}", out result, out message);
            }

            options.Add(option);
        }

        return Ok(options.ToArray(), out result, out message);
    }

    public override JsonObject OpenApiSchema(SchemaField field)
    {
        var item = new JsonObject { ["type"] = "string" };
        var options = SettingValues.List(field, FieldSettings.Options);
        if (options.Count > 0)
        {
            item["enum"] = new JsonArray([.. options.Select(option => (JsonNode)JsonValue.Create(option)!)]);
        }

        return field.Multiple
            ? new JsonObject { ["type"] = "array", ["items"] = item, ["uniqueItems"] = true }
            : item;
    }

    private static bool TryReadOption(object? raw, out string option)
    {
        switch (raw)
        {
            case string text:
                option = text;
                return true;
            case Enum member:
                option = SchemaNaming.ToCamelCase(member.ToString());
                return true;
            default:
                option = "";
                return false;
        }
    }
}
