using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Schema;

namespace FluentCMS.Admin.Forms;

// Turns what a generated form posts for a field into the field's JSON value, and a stored value back into what its widget shows
// (FRM-01). Every widget posts text; the field type then checks the value as it checks an agent's.
public static class FieldValues
{
    public const string DateTimeFormat = "yyyy-MM-ddTHH:mm";

    public static bool IsDateOnly(FieldDefinition field)
    {
        return field.Settings.ValueKind == JsonValueKind.Object
            && field.Settings.TryGetProperty("dateOnly", out var flag)
            && flag.ValueKind == JsonValueKind.True;
    }

    public static string? Setting(FieldDefinition field, string name)
    {
        return field.Settings.ValueKind == JsonValueKind.Object
            && field.Settings.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    public static IReadOnlyList<string> Strings(FieldDefinition field, string name)
    {
        return field.Settings.ValueKind == JsonValueKind.Object
            && field.Settings.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Array
                ? [.. value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!)]
                : [];
    }

    // Null when the field was left empty. A problem is set when the text cannot be the field's value at all.
    public static JsonNode? Read(FieldDefinition field, string? posted, out string? problem)
    {
        problem = null;
        var text = posted?.Trim() ?? string.Empty;

        if (field.Type == "boolean")
        {
            return JsonValue.Create(text == "true");
        }

        if (text.Length == 0)
        {
            return null;
        }

        switch (field.Type)
        {
            case "number":
                if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    return JsonValue.Create(number);
                }

                problem = "number";
                return null;

            case "dateTime" when IsDateOnly(field):
                if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    return JsonValue.Create(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                }

                problem = "date";
                return null;

            case "dateTime":
                // The browser's date-time input has no offset; the form says the time is UTC.
                if (DateTime.TryParseExact(text, [DateTimeFormat, DateTimeFormat + ":ss"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time))
                {
                    return JsonValue.Create(time.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
                }

                problem = "dateTime";
                return null;

            case "list":
            case "json":
                try
                {
                    return JsonNode.Parse(text);
                }
                catch (JsonException)
                {
                    problem = "json";
                    return null;
                }

            default:
                // Text, rich text, an enum value, or the id a reference or media field holds.
                return JsonValue.Create(posted);
        }
    }

    // What the widget shows for a stored value.
    public static string? Show(FieldDefinition field, JsonElement? value)
    {
        if (value is not { } stored || stored.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return field.Type switch
        {
            "boolean" => stored.ValueKind == JsonValueKind.True ? "true" : null,
            "number" => stored.GetRawText(),
            "dateTime" when !IsDateOnly(field) && DateTimeOffset.TryParse(stored.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
                => time.UtcDateTime.ToString(DateTimeFormat, CultureInfo.InvariantCulture),
            "list" or "json" => JsonSerializer.Serialize(stored, Indented),
            _ => stored.ValueKind == JsonValueKind.String ? stored.GetString() : stored.GetRawText(),
        };
    }

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
}
