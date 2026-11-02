using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FluentCMS.Core.Schema.FieldTypes;

// Reduces what a client or a mapper hands over to plain CLR values, so each field type has few shapes to handle.
internal static class RawValue
{
    // A JsonElement becomes null, a string, a boolean, a long or a decimal; objects and arrays stay JsonElements.
    public static object? Unwrap(object? value)
    {
        if (value is not JsonElement element)
        {
            return value;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return null;
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Number when element.TryGetInt64(out var whole):
                return whole;
            case JsonValueKind.Number when element.TryGetDecimal(out var number):
                return number;
            default:
                return element;
        }
    }

    public static string Describe(object? value) => value switch
    {
        null => "null",
        string => "a string",
        bool => "a boolean",
        JsonElement { ValueKind: JsonValueKind.Object } => "an object",
        JsonElement { ValueKind: JsonValueKind.Array } => "an array",
        JsonElement { ValueKind: JsonValueKind.Number } => "a number",
        _ when IsNumber(value) => "a number",
        _ => $"a value of type {value.GetType().Name}",
    };

    public static bool IsNumber(object value) =>
        value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;

    public static bool TryToDecimal(object value, out decimal number)
    {
        number = 0;
        try
        {
            switch (value)
            {
                case decimal d:
                    number = d;
                    return true;
                case float or double:
                    var floating = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    if (!double.IsFinite(floating))
                    {
                        return false;
                    }

                    number = (decimal)floating;
                    return true;
                case sbyte or byte or short or ushort or int or uint or long or ulong:
                    number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                    return true;
            }
        }
        catch (OverflowException)
        {
        }

        return false;
    }

    // Reads JSON from a JsonElement, a JsonNode or JSON text. A JSON null and a null value both give a null node.
    public static bool TryReadJson(object? value, out JsonNode? node, out string? message)
    {
        node = null;
        message = null;

        switch (value)
        {
            case null:
                return true;
            case JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined }:
                return true;
            case JsonElement element:
                node = JsonNode.Parse(element.GetRawText());
                return true;
            case JsonNode existing:
                node = existing.DeepClone();
                return true;
            case string text:
                try
                {
                    node = JsonNode.Parse(text);
                    return true;
                }
                catch (JsonException exception)
                {
                    message = $"is not valid JSON: {exception.Message}";
                    return false;
                }

            default:
                message = $"expected JSON but got {Describe(value)}";
                return false;
        }
    }
}
