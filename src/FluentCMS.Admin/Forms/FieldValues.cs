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

    // Null when the field was left empty. A problem is set when the text cannot be the field's value at all. A text that shows the
    // same as the stored value keeps the stored value, so a field the person did not touch is not rewritten by the widget's precision,
    // such as the seconds a date-time input drops.
    public static JsonNode? Read(FieldDefinition field, string? posted, out string? problem, JsonElement? stored = null)
    {
        problem = null;

        // Browsers send a textarea's line breaks as CRLF; stored text keeps LF. Other line separators are text and stay as typed.
        posted = Lf(posted);
        if (stored is { ValueKind: not JsonValueKind.Null } kept && posted is not null && posted == Lf(Show(field, kept)))
        {
            return JsonNode.Parse(kept.GetRawText());
        }

        var text = posted?.Trim() ?? string.Empty;

        // An unchecked box posts nothing: it is false for a stored value or a required field, and leaves an optional field that had
        // no value empty.
        if (field.Type == "boolean")
        {
            return text == "true" ? JsonValue.Create(true)
                : field.Required || stored is { ValueKind: JsonValueKind.True or JsonValueKind.False } ? JsonValue.Create(false)
                : null;
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

    private static string? Lf(string? text) => text?.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

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
