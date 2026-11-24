using System.Text.Json;
using FluentCMS.Admin.Api;

namespace FluentCMS.Admin.Administration.Members;

internal enum MemberSectionState
{
    Loading,
    Ready,
    Failed,
}

// The answers of /members and /member-groups the pages tell apart, by status, code and path only (spec 2.7).
internal static class MemberProblems
{
    // Above the caller's reach: a group's role with a grant the caller does not hold.
    public const string Forbidden = "FCMS3103";
    public const string SessionNotFound = "FCMS3047";
    public const string MemberNotFound = "FCMS3161";

    // An invalid display name, on displayName.
    public const string InvalidMember = "FCMS3162";
    public const string EmailTaken = "FCMS3163";
    public const string InvalidEmail = "FCMS3164";
    public const string GroupNotFound = "FCMS3168";

    // On name, description, or roleId for a role that no longer exists.
    public const string InvalidGroup = "FCMS3169";
    public const string GroupNameTaken = "FCMS3170";
    public const string AlreadyInGroup = "FCMS3171";
    public const string NotInGroup = "FCMS3172";

    // The instance has nowhere for the link of a new member to go.
    public const string NoMailLink = "FCMS3173";
    public const string MemberChanged = "FCMS3174";
    public const string LastWayIn = "FCMS3184";
    public const string LoginNotFound = "FCMS3185";

    // What a call may throw: a problem, or a server that did not answer or answered nonsense.
    public static bool IsFailure(Exception exception) =>
        exception is ApiProblemException or HttpRequestException or TaskCanceledException or JsonException;

    public static bool Has(Exception exception, string code) =>
        exception is ApiProblemException { Problem: var problem }
        && (problem.Code == code || problem.Errors.Any(error => error.Code == code));

    public static bool Has(Exception exception, string code, string path) =>
        exception is ApiProblemException { Problem: var problem } && problem.Errors.Any(error => error.Code == code && error.Path == path);

    // The refusal of a change by a caller who holds members.manage for the instance, which the page's gate asks for: the
    // group's role is above the caller's reach. A 403 without the code, as a proxy's, is a failure only.
    public static bool IsAboveReach(Exception exception) => Has(exception, Forbidden);
}
