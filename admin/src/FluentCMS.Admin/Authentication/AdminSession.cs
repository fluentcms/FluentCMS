using System.Globalization;
using System.Text.Json;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Configuration;
using FluentCMS.Admin.Forms;
using FluentCMS.Admin.Interop;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Admin.Authentication;

// The admin's own code flow with PKCE (spec 2.4, AQ2, decision record 0007). The access token is in memory only; the refresh
// token and the user are in the tab's sessionStorage, so a reload keeps the session and another tab signs in for itself
// (AQ3). A refresh is single flight within the tab, before the access token expires and once on a 401; one the server
// refuses ends the session: the unsaved forms are written, the tab forgets the session, and the page goes to sign in.
internal sealed class AdminSession(
    AdminConfiguration configuration,
    AuthorityClient authority,
    ISessionStorage storage,
    FormDrafts drafts,
    NavigationManager navigation,
    TimeProvider time,
    ILogger<AdminSession> logger) : IAdminSession, IAccessTokenSource
{
    public const string CallbackRoute = "authentication/login-callback";

    internal const string SessionKey = "fluentcms-admin/session";
    internal const string SignInKey = "fluentcms-admin/sign-in";
    internal const string SignedInAtKey = "fluentcms-admin/signed-in-at";

    // How long the user may take on the hosted pages, a password change and a two-factor enrolment included.
    public static readonly TimeSpan SignInLifetime = TimeSpan.FromMinutes(30);

    // An access token is refreshed this long before it expires, or at half its lifetime when that is shorter.
    public static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(1);

    // A session the server ends this soon after its sign-in completed is not signed in again on its own: a loop of silent
    // sign-ins would follow. The gate asks the user instead.
    public static readonly TimeSpan RepeatWindow = TimeSpan.FromMinutes(1);

    // How long a sign-out waits on the server, once the tab is cleared.
    public static readonly TimeSpan SignOutWait = TimeSpan.FromSeconds(3);

    private static AuthenticationJsonContext Json => AuthenticationJsonContext.Default;

    private readonly Lock _gate = new();
    private SignedInUser? _user;
    private string? _accessToken;
    private string? _refreshToken;
    private DateTimeOffset _refreshAt;
    private Task<RefreshOutcome>? _refreshing;
    private bool _redirecting;
    private bool _endedAfterSignIn;

    public event Action? Changed;

    private enum RefreshOutcome
    {
        Refreshed,
        Ended,
        Unavailable,
    }

    public SignedInUser? User => _accessToken is null ? null : _user;

    public bool SigningOut { get; private set; }

    private string RedirectUri => navigation.BaseUri + CallbackRoute;

    public async Task<bool> EnsureSignedInAsync(CancellationToken cancellationToken)
    {
        if (User is not null)
        {
            return true;
        }

        if (_endedAfterSignIn)
        {
            throw new SessionEndedAfterSignInException();
        }

        if (_redirecting)
        {
            return false;
        }

        // A value another build or page wrote, without a token or a user, is no session.
        if (await ReadAsync(SessionKey, Json.StoredSession) is { RefreshToken: { Length: > 0 }, User.Subject: { Length: > 0 } } stored)
        {
            _user = stored.User;
            _refreshToken = stored.RefreshToken;
            switch (await RefreshOnceAsync())
            {
                case RefreshOutcome.Refreshed:
                    Changed?.Invoke();
                    return true;
                case RefreshOutcome.Ended:
                    return _endedAfterSignIn ? throw new SessionEndedAfterSignInException() : false;
                default:
                    (_user, _refreshToken) = (null, null);
                    throw new HttpRequestException("The authorization server could not be reached to restore the session.");
            }
        }

        await RedirectToSignInAsync(CurrentRoute());
        return false;
    }

    // Asked for by the user, so it goes even right after a session the server ended.
    public Task SignInAsync(string? returnRoute, CancellationToken cancellationToken)
    {
        _endedAfterSignIn = false;
        return RedirectToSignInAsync(AppRoute(returnRoute));
    }

    public async Task<SignInCompletion> CompleteSignInAsync(string? code, string? state, string? error, CancellationToken cancellationToken)
    {
        // Used once, whatever follows: a second callback with the same state, a replayed code, finds nothing.
        var pending = await ReadAsync(SignInKey, Json.PendingSignIn);
        await storage.RemoveAsync(SignInKey);

        var outcome = await RedeemAsync(pending, code, state, error, cancellationToken);
        if (outcome.Failure is { } failure)
        {
            SessionLog.SignInFailed(logger, failure, error);
        }

        return outcome;
    }

    // The tab forgets the session before anything is asked of the server, so the sign-out holds whatever the server does.
    // The revocation and the discovery of the logout page then get SignOutWait at most; without them the page goes to the
    // admin's base.
    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        var refreshToken = _refreshToken ?? (await ReadAsync(SessionKey, Json.StoredSession))?.RefreshToken;
        _redirecting = true;
        SigningOut = true;
        Forget();
        await storage.RemoveAsync(SessionKey);
        await storage.RemoveAsync(SignInKey);
        await storage.RemoveAsync(SignedInAtKey);
        await storage.RemoveAsync(TabStorageKeys.Site);
        await drafts.ClearAsync();
        Changed?.Invoke();

        OpenIdConfiguration? discovery = null;
        using var timeout = new CancellationTokenSource(SignOutWait, time);
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            discovery = await authority.DiscoverAsync().WaitAsync(wait.Token);
            if (refreshToken is not null && discovery.RevocationEndpoint is { } revocation)
            {
                await authority.RevokeAsync(revocation, refreshToken, wait.Token);
            }
        }
        catch (Exception exception) when (IsUnavailable(exception) || exception is OperationCanceledException)
        {
            SessionLog.RevokeFailed(logger, exception);
        }

        // No id_token_hint: it would put the id token in the URL. The hosted logout page asks the user to confirm instead,
        // then ends the server's session and returns to the admin's base, the post-logout URI of the client.
        var signedOut = discovery?.EndSessionEndpoint is { } endSession
            ? WithQuery(endSession, new Dictionary<string, string>
            {
                ["client_id"] = configuration.ClientId,
                ["post_logout_redirect_uri"] = navigation.BaseUri,
            })
            : navigation.BaseUri;
        navigation.NavigateTo(signedOut, forceLoad: true);
    }

    public async ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is null)
        {
            return null;
        }

        if (time.GetUtcNow() >= _refreshAt)
        {
            await RefreshOnceAsync();
        }

        return _accessToken;
    }

    public async ValueTask<bool> RefreshAsync(string rejectedToken, CancellationToken cancellationToken)
    {
        // Without a refresh token (config.json without offline_access) the 401 is the answer: ending the session would send the
        // page to a sign-in that the server's session answers at once, and the next call would be refused again.
        if (_accessToken is null || _refreshToken is null)
        {
            return false;
        }

        // Another call refreshed since this one was sent.
        if (_accessToken != rejectedToken && time.GetUtcNow() < _refreshAt)
        {
            return true;
        }

        return await RefreshOnceAsync() == RefreshOutcome.Refreshed;
    }

    // A route of this app, relative to its base, or its start page: never an absolute URL, a path from the origin's root, a
    // route that leaves the base, or a page of the sign-in itself.
    internal string AppRoute(string? route)
    {
        if (string.IsNullOrEmpty(route) || route[0] is '/' or '\\' || route.Contains('\\', StringComparison.Ordinal)
            || !Uri.TryCreate(route, UriKind.Relative, out _)
            || !Uri.TryCreate(new Uri(navigation.BaseUri), route, out var target)
            || !target.AbsoluteUri.StartsWith(navigation.BaseUri, StringComparison.Ordinal))
        {
            return "";
        }

        // Under a base with a path, ".///evil.example/x" resolves inside the base and leaves "//evil.example/x", and
        // "./javascript:alert(1)" leaves "javascript:alert(1)": a browser reads either as a URL of its own. So the result may
        // not start with a slash, and its first segment may hold no colon, whatever .NET's parsing made of it.
        var relative = navigation.ToBaseRelativePath(target.AbsoluteUri);
        var path = relative.Split('?', '#')[0];
        var first = relative.Split('/', '?', '#')[0];
        return relative.StartsWith('/') || relative.StartsWith('\\') || first.Contains(':', StringComparison.Ordinal)
            || path.Equals("authentication", StringComparison.OrdinalIgnoreCase) || path.StartsWith("authentication/", StringComparison.OrdinalIgnoreCase)
            ? ""
            : relative;
    }

    private static bool IsUnavailable(Exception exception) =>
        exception is HttpRequestException or JsonException or InvalidOperationException or TaskCanceledException;

    private static string WithQuery(Uri endpoint, Dictionary<string, string> parameters)
    {
        var query = string.Join('&', parameters.Select(parameter => $"{parameter.Key}={Uri.EscapeDataString(parameter.Value)}"));
        return endpoint.AbsoluteUri + (endpoint.Query.Length > 0 ? "&" : "?") + query;
    }

    private string CurrentRoute()
    {
        var uri = navigation.Uri;
        return uri.StartsWith(navigation.BaseUri, StringComparison.Ordinal) ? AppRoute(navigation.ToBaseRelativePath(uri)) : "";
    }

    private async Task RedirectToSignInAsync(string returnRoute)
    {
        _redirecting = true;
        try
        {
            var discovery = await authority.DiscoverAsync();
            var pending = new PendingSignIn(ProofKey.NewSecret(), ProofKey.NewSecret(), ProofKey.NewSecret(), returnRoute, time.GetUtcNow());
            await storage.SetAsync(SignInKey, JsonSerializer.Serialize(pending, Json.PendingSignIn));
            navigation.NavigateTo(
                WithQuery(discovery.AuthorizationEndpoint!, new Dictionary<string, string>
                {
                    ["response_type"] = "code",
                    ["client_id"] = configuration.ClientId,
                    ["redirect_uri"] = RedirectUri,
                    ["scope"] = string.Join(' ', configuration.Scopes),
                    ["state"] = pending.State,
                    ["nonce"] = pending.Nonce,
                    ["code_challenge"] = ProofKey.Challenge(pending.Verifier),
                    ["code_challenge_method"] = "S256",
                }),
                forceLoad: true);
        }
        catch
        {
            _redirecting = false;
            throw;
        }
    }

    private async Task<SignInCompletion> RedeemAsync(PendingSignIn? pending, string? code, string? state, string? error, CancellationToken cancellationToken)
    {
        if (pending is not { State.Length: > 0, Nonce.Length: > 0, Verifier.Length: > 0 } || !ProofKey.SameSecret(state, pending.State))
        {
            return SignInCompletion.Failed(SignInFailure.UnknownState);
        }

        var now = time.GetUtcNow();
        if (now - pending.StartedAt > SignInLifetime)
        {
            return SignInCompletion.Failed(SignInFailure.Expired);
        }

        if (error is not null || string.IsNullOrEmpty(code))
        {
            return SignInCompletion.Failed(SignInFailure.Refused);
        }

        OpenIdConfiguration discovery;
        TokenAnswer answer;
        try
        {
            discovery = await authority.DiscoverAsync();
            answer = await authority.RedeemCodeAsync(code, pending.Verifier, RedirectUri, cancellationToken);
        }
        catch (TokenRefusedException)
        {
            return SignInCompletion.Failed(SignInFailure.Rejected);
        }
        catch (Exception exception) when (IsUnavailable(exception) && !cancellationToken.IsCancellationRequested)
        {
            return SignInCompletion.Failed(SignInFailure.Unreachable);
        }

        if (IdentityToken.Read(answer.IdToken, discovery.Issuer!, configuration.ClientId, pending.Nonce, now) is not { } user)
        {
            return SignInCompletion.Failed(SignInFailure.Rejected);
        }

        await drafts.KeepOnlyAsync(user.Subject);
        _user = user;
        _refreshToken = answer.RefreshToken;
        await KeepSessionAsync();
        await storage.SetAsync(SignedInAtKey, now.ToString("O", CultureInfo.InvariantCulture));
        Accept(answer);
        (_redirecting, _endedAfterSignIn) = (false, false);
        Changed?.Invoke();
        return SignInCompletion.Succeeded(AppRoute(pending.ReturnRoute));
    }

    private Task<RefreshOutcome> RefreshOnceAsync()
    {
        TaskCompletionSource<RefreshOutcome> flight;
        lock (_gate)
        {
            if (_refreshing is { } running)
            {
                return running;
            }

            flight = new TaskCompletionSource<RefreshOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
            _refreshing = flight.Task;
        }

        _ = RunAsync();
        return flight.Task;

        async Task RunAsync()
        {
            RefreshOutcome outcome;
            try
            {
                outcome = await RefreshCoreAsync();
            }
            catch (Exception exception)
            {
                lock (_gate)
                {
                    _refreshing = null;
                }

                flight.SetException(exception);
                return;
            }

            lock (_gate)
            {
                _refreshing = null;
            }

            flight.SetResult(outcome);
        }
    }

    // Shared by every caller of the flight, so no caller's cancellation stops it; the client's timeout bounds it.
    private async Task<RefreshOutcome> RefreshCoreAsync()
    {
        // The admin ends this session itself, at the expiry of an access token it cannot refresh, so it signs in again.
        if (_refreshToken is not { } refreshToken)
        {
            await EndAsync(serverEnded: false);
            return RefreshOutcome.Ended;
        }

        TokenAnswer answer;
        try
        {
            answer = await authority.RefreshAsync(refreshToken, CancellationToken.None);
        }
        catch (TokenRefusedException refused)
        {
            SessionLog.SessionEnded(logger, refused.Error);
            await EndAsync(serverEnded: true);
            return RefreshOutcome.Ended;
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            SessionLog.RefreshUnavailable(logger, exception);
            return RefreshOutcome.Unavailable;
        }

        // The rotated refresh token is kept before the access token is used, so a reload after this point has the new one.
        _refreshToken = answer.RefreshToken ?? refreshToken;
        await KeepSessionAsync();
        Accept(answer);
        return RefreshOutcome.Refreshed;
    }

    // The session is over: the unsaved forms are written under the user, the tab forgets the session, then the page goes to
    // sign in and comes back to the route, unless the server ended it right after the sign-in.
    private async Task EndAsync(bool serverEnded)
    {
        var user = _user;
        _redirecting = true;
        Forget();
        if (user is not null)
        {
            try
            {
                await drafts.SaveTrackedAsync(user.Subject);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                SessionLog.DraftsNotKept(logger, exception);
            }
        }

        await storage.RemoveAsync(SessionKey);
        if (serverEnded && await SignedInRecentlyAsync())
        {
            SessionLog.SessionEndedAfterSignIn(logger);
            (_redirecting, _endedAfterSignIn) = (false, true);
            Changed?.Invoke();
            return;
        }

        Changed?.Invoke();
        try
        {
            await RedirectToSignInAsync(CurrentRoute());
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            // The gate shows the failure and offers to try again.
            SessionLog.SignInUnavailable(logger, exception);
            Changed?.Invoke();
        }
    }

    private async Task<bool> SignedInRecentlyAsync() =>
        await storage.GetAsync(SignedInAtKey) is { } written
        && DateTimeOffset.TryParseExact(written, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var signedInAt)
        && time.GetUtcNow() - signedInAt < RepeatWindow;

    private async Task KeepSessionAsync()
    {
        if (_refreshToken is { } refreshToken && _user is { } user)
        {
            await storage.SetAsync(SessionKey, JsonSerializer.Serialize(new StoredSession(refreshToken, user), Json.StoredSession));
        }
        else
        {
            await storage.RemoveAsync(SessionKey);
        }
    }

    private void Accept(TokenAnswer answer)
    {
        var now = time.GetUtcNow();
        if (answer.ExpiresIn is { } seconds && seconds > 0)
        {
            var lifetime = TimeSpan.FromSeconds(seconds);
            _refreshAt = now + lifetime - (lifetime / 2 < RefreshMargin ? lifetime / 2 : RefreshMargin);
        }
        else
        {
            _refreshAt = DateTimeOffset.MaxValue;
        }

        _accessToken = answer.AccessToken;
    }

    private void Forget() => (_user, _accessToken, _refreshToken) = (null, null, null);

    private async Task<T?> ReadAsync<T>(string key, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
        where T : class
    {
        if (await storage.GetAsync(key) is not { Length: > 0 } json)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(json, type);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
