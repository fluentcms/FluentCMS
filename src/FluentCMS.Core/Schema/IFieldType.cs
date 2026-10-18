using System.Text.Json;
using FluentCMS.Core.Extensions;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Schema;

// A kind of value a content type field holds (SCH-02). Built-ins and plugins implement it the same way (EXT-02, EXT-N1),
// and every implementation must pass FieldTypeConformanceTests in tests/FluentCMS.Tests/Schema.
[ExtensionPoint]
public interface IFieldType
{
    // Stored in every definition that uses the type, so it never changes once released (SchemaNames.IsFieldTypeName).
    string Name { get; }

    // Checks the settings object a definition gives a field of this type and returns what is wrong, never throwing for bad input.
    // A setting the type does not know is an error with SchemaErrorCodes.SettingUnknown; FieldSettingsReader does that bookkeeping.
    // Nested types, such as the items of a list, are checked through the registry.
    IReadOnlyList<Error> ValidateSettings(JsonElement settings, FieldTypeRegistry fieldTypes);

    // The slugs of the content types a field of this type points at, so the schema can check that they exist.
    // Only called with settings that passed ValidateSettings.
    IReadOnlyList<string> ReferencedTypes(JsonElement settings, FieldTypeRegistry fieldTypes)
    {
        return [];
    }
}
