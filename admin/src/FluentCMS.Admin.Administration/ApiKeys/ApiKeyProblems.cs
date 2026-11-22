using FluentCMS.Admin.Api;
using Microsoft.Extensions.Localization;

namespace FluentCMS.Admin.Administration.ApiKeys;

// The refusals of the keys routes by what they mean, read from the status and the code only (spec 2.7).
internal static class ApiKeyProblems
{
    public const string NotFound = "FCMS3015";
    public const string NotRotatable = "FCMS3025";

    // The area's own text for a code of a field it knows, keyed by the code; the expiry's depends on the call.
    private static readonly Dictionary<string, string> FieldTexts = new(StringComparer.Ordinal)
    {
        ["FCMS3010"] = "ApiKeys.Error.Name",
        ["FCMS3004"] = "ApiKeys.Error.UnknownRole",
        ["FCMS3011"] = "ApiKeys.Error.UnknownSite",
        ["FCMS3012"] = "ApiKeys.Error.UnknownType",
        ["FCMS3014"] = "ApiKeys.Error.RateLimit",
        ["FCMS3020"] = "ApiKeys.Error.Sites",
        ["FCMS3024"] = "ApiKeys.Error.Address",
        ["FCMS3026"] = "ApiKeys.Error.Overlap",
    };

    private const string InvalidExpiry = "FCMS3013";

    // A key that is not there, or not one the caller manages.
    public static bool IsNotFound(ApiProblem problem) => problem is { Status: 404 } || problem.Code == NotFound;

    // A caller who may manage no key here, or whose roles lack a grant of the key's role as the key is bound.
    public static bool IsRefused(ApiProblem problem) => problem.Status == 403;

    public static bool HasFieldErrors(ApiProblem problem, params string[] fields) =>
        problem.Errors.Any(error => fields.Any(field => Is(error.Path, field)));

    // The error of a field: the area's text for a code it knows, the server's message otherwise, as for a code a newer
    // server adds. An expiry refused on create is one in the past, and on expire one later than the key's.
    public static string? FieldError(ApiProblem? problem, string field, IStringLocalizer localizer, bool expiryForward = false)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        if (problem?.Errors.FirstOrDefault(error => Is(error.Path, field)) is not { } error)
        {
            return null;
        }

        return error.Code switch
        {
            InvalidExpiry => localizer[expiryForward ? "ApiKeys.Error.ExpiryForward" : "ApiKeys.Error.ExpiryFuture"],
            { } code when FieldTexts.TryGetValue(code, out var key) => localizer[key],
            _ => error.Message,
        };
    }

    // A path names the field itself or an item of it, as allowedAddresses[2].
    private static bool Is(string? path, string field) =>
        path is not null && (string.Equals(path, field, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(field + "[", StringComparison.OrdinalIgnoreCase) || path.StartsWith(field + ".", StringComparison.OrdinalIgnoreCase));
}
