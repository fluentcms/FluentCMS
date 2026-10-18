using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Builtin.FieldTypes;

// An ordered list of values of one other type, whose settings are checked by that type (SCH-02).
internal sealed class ListFieldType : IFieldType
{
    public string Name => "list";

    public IReadOnlyList<Error> ValidateSettings(JsonElement settings, FieldTypeRegistry fieldTypes)
    {
        var reader = new FieldSettingsReader(Name, settings);
        var itemType = reader.RequiredString("itemType");
        var itemSettings = reader.OptionalObject("itemSettings");

        if (itemType == Name)
        {
            reader.Invalid("itemType", "cannot be 'list'; use a 'json' field for nested structures");
        }
        else if (itemType is not null)
        {
            foreach (var error in fieldTypes.ValidateSettings(itemType, itemSettings ?? FieldDefinition.EmptySettings))
            {
                reader.Add(error with { Message = $"In the list items: {error.Message}" });
            }
        }

        return reader.Errors();
    }

    // Every item is checked, so one answer names each item that is wrong.
    public IReadOnlyList<Error> ValidateValue(JsonElement value, JsonElement settings, FieldTypeRegistry fieldTypes)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            return Values.Invalid("The value must be an array.");
        }

        var itemType = ItemType(settings);
        var itemSettings = ItemSettings(settings);
        var errors = new List<Error>();
        var position = 0;

        foreach (var item in value.EnumerateArray())
        {
            position++;
            var itemErrors = item.ValueKind == JsonValueKind.Null
                ? Values.Invalid("The value must not be null.")
                : fieldTypes.ValidateValue(itemType, itemSettings, item);

            errors.AddRange(itemErrors.Select(error => error with { Message = $"Item {position}: {error.Message}" }));
        }

        return errors;
    }

    public FieldValueDescription DescribeValue(JsonElement settings, FieldTypeRegistry fieldTypes)
    {
        var item = fieldTypes.DescribeValue(ItemType(settings), ItemSettings(settings));

        return new FieldValueDescription(
            new JsonObject { ["type"] = "array", ["items"] = item.Schema },
            new JsonArray(item.Example),
            $"A list of values. Each one: {item.Description}");
    }

    public IReadOnlyList<ReferenceTarget> ReferenceTargets(JsonElement settings, FieldTypeRegistry fieldTypes)
    {
        return fieldTypes.ReferenceTargets(ItemType(settings), ItemSettings(settings));
    }

    public IReadOnlyList<ValueReference> References(JsonElement value, JsonElement settings, FieldTypeRegistry fieldTypes)
    {
        var itemType = ItemType(settings);
        var itemSettings = ItemSettings(settings);

        return value.EnumerateArray()
            .SelectMany((item, index) => fieldTypes.References(itemType, itemSettings, item)
                .Select(reference => reference with { Pointer = $"/{index}{reference.Pointer}" }))
            .ToList();
    }

    private static string ItemType(JsonElement settings)
    {
        return settings.GetProperty("itemType").GetString()!;
    }

    private static JsonElement ItemSettings(JsonElement settings)
    {
        return settings.TryGetProperty("itemSettings", out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : FieldDefinition.EmptySettings;
    }
}
