using System.Text.Json;
using FluentCMS.Admin.Api;

namespace FluentCMS.Admin.Administration.Users;

internal enum UserSectionState
{
    Loading,
    Ready,
    Failed,
}

// The answers of /users and /invitations the pages tell apart, by status, code and path only (spec 2.7).
internal static class UserProblems
{
    // Above the caller's reach: a role, or a user who holds one, with a grant the caller does not hold.
    public const string Forbidden = "FCMS3103";
    public const string InvalidEmail = "FCMS3002";
    public const string EmailTaken = "FCMS3003";
    public const string UnknownRole = "FCMS3004";
    public const string UserNotFound = "FCMS3046";
    public const string SessionNotFound = "FCMS3047";
    public const string InvitationNotFound = "FCMS3048";

    // Also a user who deactivates itself or resets its own two-factor sign-in, which the page does not offer.
    public const string InvalidUser = "FCMS3049";
    public const string RoleAlreadyHeld = "FCMS3050";
    public const string RoleNotHeld = "FCMS3051";
    public const string LastSuperAdmin = "FCMS3052";
    public const string TwoFactorNotEnabled = "FCMS3061";
    public const string SecondFactorRequired = "FCMS3064";
    public const string LastWayIn = "FCMS3090";
    public const string LoginNotFound = "FCMS3091";

    // What a call may throw: a problem, or a server that did not answer or answered nonsense.
    public static bool IsFailure(Exception exception) =>
        exception is ApiProblemException or HttpRequestException or TaskCanceledException or JsonException;

    public static bool Has(Exception exception, string code) =>
        exception is ApiProblemException { Problem: var problem }
        && (problem.Code == code || problem.Errors.Any(error => error.Code == code));

    // A code on one member of the request, such as FCMS3049 on mustChangePassword for a user without a password.
    public static bool Has(Exception exception, string code, string path) =>
        exception is ApiProblemException { Problem: var problem } && problem.Errors.Any(error => error.Code == code && error.Path == path);

    // The refusal of a change by a caller who holds users.manage for the instance, which the page's gate asks for: the target,
    // or a role the change names, is above the caller's reach. A 403 without the code, as a proxy's, is a failure only.
    public static bool IsAboveReach(Exception exception) => Has(exception, Forbidden);
}
