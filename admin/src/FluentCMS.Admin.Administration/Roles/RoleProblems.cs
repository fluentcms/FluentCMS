using System.Text.Json;
using FluentCMS.Admin.Api;

namespace FluentCMS.Admin.Administration.Roles;

// The answers of /roles the pages tell apart, by status, code and path only (spec 2.7).
internal static class RoleProblems
{
    // A role that grants more than the caller holds, made, changed or deleted; or roles.manage not held where it applies.
    public const string Forbidden = "FCMS3103";
    public const string RoleNotFound = "FCMS3040";

    // A name that is empty, too long, taken where the role applies, or the new name of a built-in role.
    public const string InvalidRole = "FCMS3041";

    // A grant whose permission or constraints the server refuses, on grants[i].permission or grants[i].constraints.*.
    public const string InvalidGrant = "FCMS3042";

    // A built-in role deleted, or Super Admin changed beyond requireTwoFactor.
    public const string RoleFixed = "FCMS3043";

    // A role still held by a user, a key that is not revoked or a member group.
    public const string RoleInUse = "FCMS3044";

    // Another change of the role was saved during this one.
    public const string RoleChanged = "FCMS3045";
    public const string LastSuperAdmin = "FCMS3052";

    public static bool IsFailure(Exception exception) =>
        exception is ApiProblemException or HttpRequestException or TaskCanceledException or JsonException;

    public static bool Has(Exception exception, string code) =>
        exception is ApiProblemException { Problem: var problem }
        && (problem.Code == code || Errors(problem).Any(error => error.Code == code));

    // A 403 of the Management API carries FCMS3103; one without it, as a proxy's, is a failure only.
    public static bool IsAboveReach(Exception exception) => Has(exception, Forbidden);

    // The index of the grant each refusal of a grant is on, from its path grants[i]...
    public static IEnumerable<int> RefusedGrants(Exception exception) =>
        exception is ApiProblemException { Problem: var problem }
            ? Errors(problem).Select(error => GrantIndex(error.Path)).OfType<int>().Distinct()
            : [];

    public static bool OnPath(Exception exception, string code, string path) =>
        exception is ApiProblemException { Problem: var problem } && Errors(problem).Any(error => error.Code == code && error.Path == path);

    // A problem without errors, as a 403's, may read as none at all.
    private static IReadOnlyList<ApiProblemError> Errors(ApiProblem problem) => problem.Errors ?? [];

    private static int? GrantIndex(string? path)
    {
        const string Prefix = "grants[";
        if (path is null || !path.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var end = path.IndexOf(']', StringComparison.Ordinal);
        return end > Prefix.Length && int.TryParse(path.AsSpan(Prefix.Length, end - Prefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var index)
            ? index
            : null;
    }
}
