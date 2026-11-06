using System.Globalization;
using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Content.Query;
using Microsoft.AspNetCore.Http;

namespace FluentCMS.Core.Api.Management;

// A request body of the groups beside content, read key by key. A key the route does not take is refused, and every error of
// the body is reported at once.
internal sealed class BodyReader
{
    private readonly JsonElement _body;
    private readonly List<ValidationError> _errors = [];

    public BodyReader(JsonElement body, params string[] keys)
    {
        _body = body;
        _errors.AddRange(body.EnumerateObject()
            .Where(property => !keys.Contains(property.Name, StringComparer.Ordinal))
            .Select(property => new ValidationError(property.Name, ValidationErrorCodes.InvalidValue, $"'{property.Name}' is not part of this request.")));
    }

    public static async Task<BodyReader> ReadAsync(HttpRequest request, params string[] keys) => new(await RequestBody.ObjectAsync(request), keys);

    public bool Has(string key) => _body.TryGetProperty(key, out _);

    public string? String(string key, bool required = false) => Read(key, required, "a string", value =>
        value.ValueKind == JsonValueKind.String ? (true, value.GetString()) : (false, null));

    public bool? Boolean(string key) => Read<bool?>(key, required: false, "true or false", value =>
        value.ValueKind is JsonValueKind.True or JsonValueKind.False ? (true, value.GetBoolean()) : (false, null));

    public int? Integer(string key) => Read<int?>(key, required: false, "a whole number", value =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? (true, number) : (false, null));

    public Guid? Id(string key, bool required = false) => Read<Guid?>(key, required, "an id", value =>
        value.ValueKind == JsonValueKind.String && Guid.TryParseExact(value.GetString(), "D", out var id) ? (true, id) : (false, null));

    public DateTimeOffset? Moment(string key) => Read<DateTimeOffset?>(key, required: false, "an ISO 8601 date and time with an offset, such as 2027-03-01T09:00:00Z", value =>
        value.ValueKind == JsonValueKind.String && value.TryGetDateTimeOffset(out var moment) && ManagementQuery.HasOffset(value.GetString()!) ? (true, moment) : (false, null));

    public IReadOnlyList<Guid>? Ids(string key, bool required = false) => Read<IReadOnlyList<Guid>>(key, required, "an array of ids", value =>
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            return (false, null);
        }

        List<Guid> ids = [];
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || !Guid.TryParseExact(item.GetString(), "D", out var id))
            {
                return (false, null);
            }

            ids.Add(id);
        }

        return (true, ids);
    });

    public IReadOnlyList<string>? Strings(string key) => Read<IReadOnlyList<string>>(key, required: false, "an array of strings", value =>
        value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String)
            ? (true, [.. value.EnumerateArray().Select(item => item.GetString()!)])
            : (false, null));

    public JsonElement? Element(string key, bool required = false) =>
        _body.TryGetProperty(key, out var value) ? value : Missing<JsonElement?>(key, required);

    public void Error(string path, string message) => _errors.Add(new ValidationError(path, ValidationErrorCodes.InvalidValue, message));

    public void ThrowIfInvalid()
    {
        if (_errors.Count > 0)
        {
            throw new ValidationException(_errors);
        }
    }

    // A null value is no value, so an optional key may be sent as null.
    private T? Read<T>(string key, bool required, string expected, Func<JsonElement, (bool Ok, T? Value)> read)
    {
        if (!_body.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return Missing<T>(key, required);
        }

        var (ok, result) = read(value);
        if (!ok)
        {
            Error(key, $"'{key}' is {expected}.");
        }

        return result;
    }

    private T? Missing<T>(string key, bool required)
    {
        if (required)
        {
            _errors.Add(new ValidationError(key, ValidationErrorCodes.Required, $"'{key}' is required."));
        }

        return default;
    }

}

// The query string of the groups beside content: the bridge's site and locale, and the parameters a route names. A list
// takes page and pageSize, which work as they do on content.
internal static class ManagementQuery
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;

    private static readonly string[] Bridge = [RequestBridge.SiteParameter, RequestBridge.LocaleParameter];

    public static void Only(HttpRequest request, params string[] names)
    {
        var refused = request.Query.Keys
            .Where(key => !Bridge.Contains(key, StringComparer.Ordinal) && !names.Contains(key, StringComparer.Ordinal))
            .Select(key => new ValidationError(key, QueryErrorCodes.InvalidParameter, $"'{key}' does not apply to this request."))
            .ToList();
        if (refused.Count > 0)
        {
            throw new ValidationException(refused);
        }
    }

    public static (int Page, int PageSize) Page(HttpRequest request)
    {
        var page = Number(request, "page") ?? 1;
        var size = Number(request, "pageSize") ?? DefaultPageSize;
        if (page < 1)
        {
            throw Invalid("page", "The page number starts at 1.");
        }

        if (size < 1 || size > MaxPageSize)
        {
            throw Invalid("pageSize", $"The page size is from 1 to {MaxPageSize}.");
        }

        return (page, size);
    }

    public static string? Text(HttpRequest request, string name)
    {
        var values = request.Query[name];
        return values.Count switch
        {
            0 => null,
            1 when !string.IsNullOrEmpty(values[0]) => values[0],
            _ => throw Invalid(name, $"'{name}' takes one value."),
        };
    }

    public static Guid? Id(HttpRequest request, string name) =>
        Text(request, name) is { } text
            ? Guid.TryParseExact(text, "D", out var id) ? id : throw Invalid(name, $"'{text}' is not an id.")
            : null;

    public static DateTimeOffset? Moment(HttpRequest request, string name) =>
        Text(request, name) is { } text
            ? DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var moment) && HasOffset(text)
                ? moment
                : throw Invalid(name, $"'{text}' is not an ISO 8601 date and time, such as 2027-03-01T09:00:00Z.")
            : null;

    public static TEnum? Enum<TEnum>(HttpRequest request, string name)
        where TEnum : struct, Enum =>
        Text(request, name) is { } text
            ? System.Enum.GetValues<TEnum>().Cast<TEnum?>().FirstOrDefault(value => string.Equals(JsonNamingPolicy.CamelCase.ConvertName(value!.Value.ToString()), text, StringComparison.Ordinal))
                ?? throw Invalid(name, $"'{text}' is not one of {string.Join(", ", System.Enum.GetValues<TEnum>().Select(value => JsonNamingPolicy.CamelCase.ConvertName(value.ToString())))}.")
            : null;

    // An instant names its offset, Z included, rather than taking the server's.
    public static bool HasOffset(string text) =>
        text.Contains('T', StringComparison.Ordinal) && (text.EndsWith('Z') || text.EndsWith('z') || text.LastIndexOfAny(['+', '-']) > text.IndexOf('T', StringComparison.Ordinal));

    public static Guid RouteId(string value, string name) =>
        Guid.TryParseExact(value, "D", out var id) ? id : throw new ValidationException([new ValidationError(name, QueryErrorCodes.InvalidValue, $"'{value}' is not an id.")]);

    private static int? Number(HttpRequest request, string name) =>
        Text(request, name) is { } text
            ? int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : throw Invalid(name, $"'{name}' is a whole number.")
            : null;

    private static ValidationException Invalid(string name, string message) => new([new ValidationError(name, QueryErrorCodes.InvalidValue, message)]);
}
