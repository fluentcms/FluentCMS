using System.Net;
using System.Text.Json;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Authentication;
using FluentCMS.Admin.Interop;
using Microsoft.AspNetCore.Components;

namespace FluentCMS.Admin.Administration.Account;

// After a change that gives the user a new security stamp, the tab signs in again through the session to come back here. False
// when the authorization server could not be reached, which the page then says, as for a failed action.
internal static class AccountSignIn
{
    public static async Task<bool> AgainAsync(IAdminSession session)
    {
        try
        {
            await session.SignInAsync(AccountPage.Route, CancellationToken.None);
            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException or TaskCanceledException)
        {
            return false;
        }
    }

    // For a change that needs a recent sign-in: the hosted pages show the login page again, and the tab comes back to the
    // route. False as above. A browser that refuses the tab's storage cannot keep the sign-in's secrets, so the page is loaded
    // again, and the gate there says so.
    public static async Task<bool> RecentlyAsync(IAdminSession session, NavigationManager navigation, string returnRoute)
    {
        try
        {
            await session.ReauthenticateAsync(returnRoute, CancellationToken.None);
            return true;
        }
        catch (TabStorageRefusedException)
        {
            navigation.NavigateTo(navigation.BaseUri + AccountPage.Route, forceLoad: true);
            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException or TaskCanceledException)
        {
            return false;
        }
    }
}

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

// The answers of the /me and /mcp/consents routes the account page tells apart, by status and code only (spec 2.7).
internal static class AccountProblems
{
    public const string PluginDisabled = "FCMS1550";
    public const string ConsentNotFound = "FCMS1603";
    public const string ConsentChangedMeanwhile = "FCMS1609";
    public const string ConsentsBusy = "FCMS3106";
    public const string SessionNotFound = "FCMS3047";
    public const string TwoFactorEnabled = "FCMS3060";
    public const string TwoFactorNotEnabled = "FCMS3061";
    public const string NoAuthenticator = "FCMS3062";
    public const string InvalidTwoFactorCode = "FCMS3063";
    public const string RecentSignInRequired = "FCMS3065";
    public const string LastWayIn = "FCMS3090";
    public const string LoginNotFound = "FCMS3091";

    // What a call of the page may throw: a problem, or a server that did not answer or answered nonsense.
    public static bool IsFailure(Exception exception) =>
        exception is ApiProblemException or HttpRequestException or TaskCanceledException or JsonException;

    // A key or a service account, which has no two-factor sign-in, logins or preferences.
    public static bool IsRefused(Exception exception) => Status(exception) == HttpStatusCode.Forbidden;

    // The token of the tab was refused and could not be refreshed: the session's own path signs in again.
    public static bool IsSignedOut(Exception exception) => Status(exception) == HttpStatusCode.Unauthorized;

    // A route of a plugin that is not installed, which answers a 404 of no problem, or one disabled in the site.
    public static bool IsPluginOff(Exception exception) =>
        exception is ApiProblemException { Problem: { Status: (int)HttpStatusCode.NotFound } problem }
        && (problem.Code is null || problem.Code == PluginDisabled);

    public static bool Has(Exception exception, string code) =>
        exception is ApiProblemException { Problem: var problem }
        && (problem.Code == code || problem.Errors.Any(error => error.Code == code));

    private static HttpStatusCode? Status(Exception exception) =>
        exception is ApiProblemException { Problem.Status: var status } ? (HttpStatusCode)status : null;
}
