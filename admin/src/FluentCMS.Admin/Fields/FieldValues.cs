using System.Globalization;
using System.Net.Mail;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;

namespace FluentCMS.Admin.Fields;

// How the core editors and cells read and write the JSON of a field's value, as the Management API stores it: a date as
// yyyy-MM-dd, a date and time as an instant in UTC, numbers as JSON numbers. What is sent is the same in every culture.
internal static class FieldValues
{
    public const string DateFormat = "yyyy-MM-dd";
    public const string MomentFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    public static string? Text(JsonNode? value) =>
        value is JsonValue scalar && scalar.GetValueKind() == JsonValueKind.String ? scalar.GetValue<string>() : null;

    // Empty text is no value, so that an optional email or URL left empty is not refused as malformed.
    public static JsonNode? FromText(string? text) => string.IsNullOrEmpty(text) ? null : JsonValue.Create(text);

    // By its JSON text, which reads the same whether the value was parsed or made from a long or a decimal.
    public static decimal? Number(JsonNode? value) =>
        value is JsonValue scalar && scalar.GetValueKind() == JsonValueKind.Number
        && decimal.TryParse(scalar.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;

    public static bool? Boolean(JsonNode? value) =>
        value is JsonValue scalar && scalar.GetValueKind() is JsonValueKind.True or JsonValueKind.False ? scalar.GetValue<bool>() : null;

    public static DateOnly? Date(JsonNode? value) =>
        DateOnly.TryParseExact(Text(value), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    public static JsonNode? FromDate(DateOnly? date) => date is { } day ? JsonValue.Create(day.ToString(DateFormat, CultureInfo.InvariantCulture)) : null;

    // Text without an offset is UTC, as the server reads it.
    public static DateTimeOffset? Moment(JsonNode? value) =>
        DateTimeOffset.TryParse(Text(value), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var moment) ? moment : null;

    public static JsonNode FromMoment(DateTimeOffset moment) => JsonValue.Create(moment.UtcDateTime.ToString(MomentFormat, CultureInfo.InvariantCulture));

    public static IReadOnlyList<string> Strings(JsonNode? value) => value switch
    {
        JsonArray items => [.. items.Select(Text).OfType<string>()],
        _ when Text(value) is { } single => [single],
        _ => [],
    };

    // The options of a select field, as its settings list them.
    public static IReadOnlyList<string> Options(SchemaFieldDescription field) =>
        field.Settings["options"] is JsonArray options ? [.. options.Select(Text).OfType<string>()] : [];

    // As the server checks them (EmailFieldType and UrlFieldType), so that what is refused here is refused there.
    public static bool IsEmail(string text) =>
        text.Length <= 254
        && !text.Any(char.IsWhiteSpace)
        && MailAddress.TryCreate(text, out var address)
        && address.DisplayName.Length == 0
        && string.Equals(address.Address, text, StringComparison.OrdinalIgnoreCase);

    public static bool IsUrl(string text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.Host.Length > 0;

    // An error of the editor and the errors of the last save, which may both stand.
    public static string? Errors(string? own, string? saved) =>
        own is null ? saved : saved is null ? own : own + " " + saved;
}
