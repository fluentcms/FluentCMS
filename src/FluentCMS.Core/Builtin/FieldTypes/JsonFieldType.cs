using System.Text.Json;
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
}
