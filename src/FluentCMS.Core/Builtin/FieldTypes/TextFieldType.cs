using System.Text.Json;
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
}
