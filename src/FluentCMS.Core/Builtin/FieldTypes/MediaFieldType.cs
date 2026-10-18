using System.Text.Json;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Builtin.FieldTypes;

// An asset from the media library; its settings arrive with the library in F-MED.
internal sealed class MediaFieldType : IFieldType
{
    public string Name => "media";

    public IReadOnlyList<Error> ValidateSettings(JsonElement settings, FieldTypeRegistry fieldTypes)
    {
        return new FieldSettingsReader(Name, settings).Errors();
    }
}
