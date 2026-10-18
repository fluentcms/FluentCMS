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

    // Checks one value of a field with these settings, as an entry stores it (CNT-02), and returns errors with
    // SchemaErrorCodes.ValueInvalid, never throwing for bad input. Only called with settings that passed ValidateSettings and
    // a value that is not null. It accepts what DescribeValue documents, and nested values are checked through the registry.
    IReadOnlyList<Error> ValidateValue(JsonElement value, JsonElement settings, FieldTypeRegistry fieldTypes);

    // How a value of a field with these settings looks, for the schema document agents read (SCH-06).
    // Only called with settings that passed ValidateSettings; the example must satisfy the schema.
    FieldValueDescription DescribeValue(JsonElement settings, FieldTypeRegistry fieldTypes);

    // What a field of this type points at, so the schema can check that the types and fields exist (SCH-04).
    // Only called with settings that passed ValidateSettings.
    IReadOnlyList<ReferenceTarget> ReferenceTargets(JsonElement settings, FieldTypeRegistry fieldTypes)
    {
        return [];
    }

    // The references one stored value makes, which the reference index holds so nothing reads them out of the JSON (CNT-03,
    // ADR-0005). A type that declares ReferenceTargets returns one, to one of those targets, for every place its value points at.
    // Only called with settings and a value that passed validation.
    IReadOnlyList<ValueReference> References(JsonElement value, JsonElement settings, FieldTypeRegistry fieldTypes)
    {
        return [];
    }
}
