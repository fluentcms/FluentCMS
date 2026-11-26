namespace FluentCMS.Admin.Authentication;

// The sign-in of the tab, as the shell uses it (A-08): who is signed in, signing in and signing out. The callback page and
// the gate of AdminApp use the rest.
internal interface IAdminSession
{
    // Null while nobody is signed in.
    SignedInUser? User { get; }

    // True from the moment a sign-out has cleared the tab until the page leaves for the logout page.
    bool SigningOut { get; }

    // Raised when a user signs in or the session ends.
    event Action? Changed;

    // True when the tab has a session, restored from its refresh token if it must be. Otherwise the whole page goes to the
    // authorize endpoint to come back to the current route, and the answer is false.
    Task<bool> EnsureSignedInAsync(CancellationToken cancellationToken);

    // Redirects the whole page to the authorize endpoint; after sign-in the admin returns to the route, one of its own.
    Task SignInAsync(string? returnRoute, CancellationToken cancellationToken);

    // Redirects the whole page to the hosted page that changes the password. The next start of the tab, the return from it,
    // signs in again if its session has ended, even right after its sign-in.
    Task ChangePasswordAsync(string pageUrl, CancellationToken cancellationToken);

    // The callback's parameters, as the authorize endpoint sent them.
    Task<SignInCompletion> CompleteSignInAsync(string? code, string? state, string? error, CancellationToken cancellationToken);

    // Clears what the tab keeps, then revokes the refresh token and redirects the whole page to end the server's session.
    Task SignOutAsync(CancellationToken cancellationToken);
}

internal enum SignInFailure
{
    // The callback carries a state this tab did not issue, or the one it issued was used already.
    UnknownState,

    // The sign-in was started too long ago.
    Expired,

    // The authorize endpoint answered with an error, such as access_denied.
    Refused,

    // The token endpoint refused the code, or its answer or id token was not the one of this sign-in.
    Rejected,

    // The authorization server could not be reached.
    Unreachable,

    // The browser refused the tab's sessionStorage, which holds the secrets of the sign-in and the session.
    StorageRefused,
}

// The server ended the session within AdminSession.RepeatWindow of its sign-in, so the tab does not sign in again on its own;
// SignInAsync, which the user asks for, does, and so does the return from ChangePasswordAsync.
internal sealed class SessionEndedAfterSignInException()
    : Exception("The server ended the session right after the sign-in; the tab waits for the user before it signs in again.");

// Succeeded with the admin route to return to, or failed with the reason.
internal sealed record SignInCompletion(string? ReturnRoute, SignInFailure? Failure)
{
    public static SignInCompletion Succeeded(string returnRoute) => new(returnRoute, null);

    public static SignInCompletion Failed(SignInFailure failure) => new(null, failure);
}
