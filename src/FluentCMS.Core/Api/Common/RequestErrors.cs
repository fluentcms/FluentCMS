using FluentCMS.Content;
using FluentCMS.Core.Content.Query;
using Microsoft.AspNetCore.Http;

namespace FluentCMS.Core.Api.Common;

// The engine names a field in some of its errors, where a client needs the parameter it sent: limit rather than
// pageSize, field or value on a by/ route. A filter condition parsed with its source already names its parameter;
// one that is not found by its field.
internal static class RequestErrors
{
    public const string Field = "field";
    public const string Value = "value";

    private static readonly HashSet<string> Named = new(StringComparer.Ordinal)
    {
        "", "type", "id", Field, Value, "status", "locale", "site", "fallback", "richText", "cursor", "fields", "populate", "depth", "sort", "page", "pageSize", "offset", "limit", "withTotal", "filter",
    };

    // byField is the field of a by/{field}/{value} route, whose filter the client did not write as a parameter.
    public static ValidationException ForRequest(ValidationException exception, HttpRequest request, string? byField = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(request);

        return new(exception.Errors.Select(error => error with { Path = PathOf(error, request, byField) }));
    }

    // An unknown type and one the principal cannot read are the same 404, decided before the query runs so that an
    // error on a field that happens to be named type is not taken for it.
    public static ValidationException TypeNotFound(string type) =>
        new([new ValidationError("type", ApiErrorCodes.NotFound, $"There is no content type '{type}'.")]);

    private static string PathOf(ValidationError error, HttpRequest request, string? byField)
    {
        var path = error.Path;
        if (request.Query.ContainsKey(path))
        {
            return path;
        }

        if (FilterKey(request, path) is { } key)
        {
            return key;
        }

        if (byField is not null && Covers(byField, path))
        {
            return error.Code == QueryErrorCodes.InvalidValue ? Value : Field;
        }

        return path switch
        {
            "pageSize" when request.Query.ContainsKey("limit") => "limit",
            "page" when request.Query.ContainsKey("offset") => "offset",
            _ when Named.Contains(path) => path,
            _ => "filter",
        };
    }

    // The first filter parameter whose field is the path, or holds it.
    private static string? FilterKey(HttpRequest request, string path) =>
        request.Query.Keys
            .Where(key => key.StartsWith("filter[", StringComparison.Ordinal) && key.EndsWith(']'))
            .Select(key => (Key: key, Parts: key[7..^1].Split("][")))
            .Where(item => item.Parts.Length >= 2 && Covers(item.Parts[^2], path))
            .Select(item => item.Key)
            .FirstOrDefault();

    private static bool Covers(string field, string path) =>
        field == path
        || path.StartsWith(field + ".", StringComparison.Ordinal)
        || field.StartsWith(path + ".", StringComparison.Ordinal);
}
