using System.Text.Json;
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
}
