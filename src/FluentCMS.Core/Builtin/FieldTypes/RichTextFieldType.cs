using System.Text.Json;
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
}
