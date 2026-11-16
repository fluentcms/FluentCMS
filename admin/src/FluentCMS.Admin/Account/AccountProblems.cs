using System.Net;
using System.Text.Json;
using FluentCMS.Admin.Api;

namespace FluentCMS.Admin.Account;

internal enum AccountSectionState
{
    Loading,
    Ready,
    Failed,

    // The caller is a key or a service account (403).
    Refused,

    // This tab's token was refused (401).
    SignedOut,
}

// The answers of the /me routes the account page tells apart, by status and code only (spec 2.7).
internal static class AccountProblems
{
    public const string SessionNotFound = "FCMS3047";
    public const string TwoFactorEnabled = "FCMS3060";
    public const string TwoFactorNotEnabled = "FCMS3061";
    public const string NoAuthenticator = "FCMS3062";
    public const string InvalidTwoFactorCode = "FCMS3063";
    public const string LastWayIn = "FCMS3090";
    public const string LoginNotFound = "FCMS3091";

    // What a call of the page may throw: a problem, or a server that did not answer or answered nonsense.
    public static bool IsFailure(Exception exception) =>
        exception is ApiProblemException or HttpRequestException or TaskCanceledException or JsonException;

    // A key or a service account, which has no two-factor sign-in, logins or preferences.
    public static bool IsRefused(Exception exception) => Status(exception) == HttpStatusCode.Forbidden;

    // The token of the tab was refused and could not be refreshed: the session's own path signs in again.
    public static bool IsSignedOut(Exception exception) => Status(exception) == HttpStatusCode.Unauthorized;

    public static bool Has(Exception exception, string code) =>
        exception is ApiProblemException { Problem: var problem }
        && (problem.Code == code || problem.Errors.Any(error => error.Code == code));

    private static HttpStatusCode? Status(Exception exception) =>
        exception is ApiProblemException { Problem.Status: var status } ? (HttpStatusCode)status : null;
}
