using System.Text.Json;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Builtin.FieldTypes;

// Points at an entry of the content type named by 'target'. Whether that type exists is checked where types are stored.
internal sealed class ReferenceFieldType : IFieldType
{
    public string Name => "reference";

    public IReadOnlyList<Error> ValidateSettings(JsonElement settings, FieldTypeRegistry fieldTypes)
    {
        var reader = new FieldSettingsReader(Name, settings);
        var target = reader.RequiredString("target");

        if (target is not null && !SchemaNames.IsSlug(target))
        {
            reader.Invalid("target", "must be the slug of a content type, such as 'product'");
        }

        return reader.Errors();
    }

    public IReadOnlyList<string> ReferencedTypes(JsonElement settings, FieldTypeRegistry fieldTypes)
    {
        return [settings.GetProperty("target").GetString()!];
    }
}
