using System.Text.Json;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Builtin.FieldTypes;

internal static class Values
{
    public static IReadOnlyList<Error> Invalid(string message)
    {
        return [new Error(SchemaErrorCodes.ValueInvalid, message)];
    }

    public static IReadOnlyList<Error> Text(JsonElement value, int? maxLength)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            return Invalid("The value must be a string.");
        }

        var length = Length(value.GetString()!);
        return length > maxLength ? Invalid($"The value must be at most {maxLength} characters, and it has {length}.") : [];
    }

    // The 8-4-4-4-12 form the schema document's "uuid" format names.
    public static bool IsId(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.String && Guid.TryParseExact(value.GetString(), "D", out _);
    }

    // Counted in code points, as JSON Schema's maxLength counts them, so the limit agents read is the limit applied.
    private static int Length(string text)
    {
        return text.EnumerateRunes().Count();
    }
}
