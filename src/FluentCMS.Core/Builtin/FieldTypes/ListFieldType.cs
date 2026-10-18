using System.Text.Json;
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
}
