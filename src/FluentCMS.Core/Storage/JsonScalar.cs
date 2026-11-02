using System.Linq.Expressions;
using FluentCMS.Core.Schema.FieldTypes;

namespace FluentCMS.Core.Storage;

// How a scalar inside JSON is compared. A JSON extraction keeps the value's own type, so each kind has its own comparison.
public enum JsonScalarType
{
    Text,
    WholeNumber,
    DecimalNumber,
    Boolean,
}

public static class JsonScalar
{
    // The comparison for a field type, or null if the type has no scalar value inside JSON. Dates are compared as
    // the ISO 8601 text that the JSON holds.
    public static JsonScalarType? TypeOf(string fieldType) => fieldType switch
    {
        FieldTypeNames.Text or FieldTypeNames.LongText or FieldTypeNames.Email or FieldTypeNames.Url or FieldTypeNames.Slug
            or FieldTypeNames.Select or FieldTypeNames.Date or FieldTypeNames.DateTime => JsonScalarType.Text,
        FieldTypeNames.Integer => JsonScalarType.WholeNumber,
        FieldTypeNames.Decimal => JsonScalarType.DecimalNumber,
        FieldTypeNames.Boolean => JsonScalarType.Boolean,
        _ => null,
    };

    public static Type ClrType(JsonScalarType type) => type switch
    {
        JsonScalarType.Text => typeof(string),
        JsonScalarType.WholeNumber => typeof(long),
        JsonScalarType.DecimalNumber => typeof(decimal),
        JsonScalarType.Boolean => typeof(bool),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    // Q7 caps paths at one level. Dialects put the result into a path or key literal, so only a plain name may pass.
    public static string CheckField(string field)
    {
        ArgumentException.ThrowIfNullOrEmpty(field);

        return field.All(character => char.IsAsciiLetterOrDigit(character) || character == '_')
            ? field
            : throw new ArgumentException($"'{field}' is not a one-level JSON path.", nameof(field));
    }

    public static void CheckValue(JsonScalarType type, Expression value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Type != ClrType(type))
        {
            throw new ArgumentException($"A {type} comparison needs a {ClrType(type).Name} value, not {value.Type.Name}.", nameof(value));
        }
    }
}
