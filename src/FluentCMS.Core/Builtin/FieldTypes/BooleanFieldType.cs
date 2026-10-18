using System.Text.Json;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Builtin.FieldTypes;

internal sealed class BooleanFieldType : IFieldType
{
    public string Name => "boolean";

    public IReadOnlyList<Error> ValidateSettings(JsonElement settings, FieldTypeRegistry fieldTypes)
    {
        return new FieldSettingsReader(Name, settings).Errors();
    }
}
