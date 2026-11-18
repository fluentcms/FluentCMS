using System.Buffers.Text;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Admin.Authentication;
using FluentCMS.Admin.Interop;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace FluentCMS.Admin.Tests.Authentication;

// The tab's sessionStorage, which outlives a page load of the tab, with the order of its writes.
internal sealed class MemorySessionStorage : ISessionStorage
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, string> _items = new(StringComparer.Ordinal);

    public List<string> Writes { get; } = [];

    public IReadOnlyDictionary<string, string> Items
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<string, string>(_items);
            }
        }
    }

    public string? this[string key] => Items.GetValueOrDefault(key);

    // The sessionStorage of a tab duplicated from this one, or opened by it.
    public MemorySessionStorage Copy()
    {
        var copy = new MemorySessionStorage();
        foreach (var (key, value) in Items)
        {
            copy._items[key] = value;
        }

        return copy;
    }

    public ValueTask<string?> GetAsync(string key)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(_items.GetValueOrDefault(key));
        }
    }

    public ValueTask SetAsync(string key, string value)
    {
        lock (_gate)
        {
            _items[key] = value;
            Writes.Add($"set {key}");
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveAsync(string key)
    {
        lock (_gate)
        {
            if (_items.Remove(key))
            {
                Writes.Add($"remove {key}");
            }
        }

        return ValueTask.CompletedTask;
    }
}

// The admin tabs of one browser, each by its sessionStorage: the last page load of a tab holds the tab id it was given.
internal sealed class BrowserTabs
{
    private readonly Lock _gate = new();
    private readonly Dictionary<MemorySessionStorage, string> _held = [];

    public ITabChannel ChannelOf(MemorySessionStorage tab) => new Channel(this, tab);

    public void Close(MemorySessionStorage tab)
    {
        lock (_gate)
        {
            _held.Remove(tab);
        }
    }

    private sealed class Channel(BrowserTabs browser, MemorySessionStorage tab) : ITabChannel
    {
        public ValueTask<bool> IsHeldElsewhereAsync(string tabId)
        {
            lock (browser._gate)
            {
                return ValueTask.FromResult(browser._held.Any(held => held.Key != tab && held.Value == tabId));
            }
        }

        public ValueTask HoldAsync(string tabId)
        {
            lock (browser._gate)
            {
                browser._held[tab] = tabId;
            }

            return ValueTask.CompletedTask;
        }
    }
}

internal sealed record AuthorityRequest(string Path, Dictionary<string, string> Form)
{
    public string? this[string name] => Form.GetValueOrDefault(name);
}

// The authorization server of https://cms.example.com as the admin sees it: its discovery document, a token endpoint that
// redeems the one code it issued and rotates refresh tokens, and the revocation endpoint. A test changes what it answers.
internal sealed class FakeAuthority : HttpMessageHandler
{
    public const string Issuer = "https://cms.example.com/";
    public const string Code = "code-of-the-sign-in";
    public const string ClientId = "fluentcms-admin";

    private readonly Lock _gate = new();
    private readonly HashSet<string> _refreshTokens = new(StringComparer.Ordinal);
    private bool _codeUsed;
    private int _issued;

    public List<AuthorityRequest> Requests { get; } = [];

    public string DiscoveredIssuer { get; set; } = Issuer;

    // The nonce the id token carries; a test sets it from the authorize request, or to another to see it refused.
    public string? Nonce { get; set; }

    public string Subject { get; set; } = "0193a1b2-0000-7000-8000-000000000001";

    public string Audience { get; set; } = ClientId;

    public int ExpiresIn { get; set; } = 3600;

    // An error the token endpoint answers a refresh with, such as invalid_grant.
    public string? RefreshError { get; set; }

    public HttpStatusCode RevokeStatus { get; set; } = HttpStatusCode.OK;

    // The revocation waits for it, or until the client gives up.
    public TaskCompletionSource? HoldRevoke { get; set; }

    // A status the token endpoint answers a refresh with instead of tokens, such as a 503.
    public HttpStatusCode? RefreshStatus { get; set; }

    // The refresh fails as a network failure does.
    public bool RefreshFailsOnTheNetwork { get; set; }

    // As for a client whose scopes lack offline_access.
    public bool IssuesRefreshTokens { get; set; } = true;

    // The discovery document never comes, until the client gives up.
    public bool DiscoveryHangs { get; set; }

    // Replaces the discovery document.
    public JsonObject? Discovery { get; set; }

    // A refresh waits for it, so that a test can start several calls while one is in flight.
    public TaskCompletionSource? HoldRefresh { get; set; }

    // Characters added after the number of each access token, so that comparing two of them takes a while.
    public int AccessTokenPadding { get; set; }

    public int Refreshes => Requests.Count(request => request["grant_type"] == "refresh_token");

    public string? LastAccessToken { get; private set; }

    public string? LastRefreshToken { get; private set; }

    public static string IdToken(object claims)
    {
        static string Part(string json) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json));
        return $"{Part("""{"alg":"RS256","typ":"JWT"}""")}.{Part(JsonSerializer.Serialize(claims))}.c2lnbmF0dXJl";
    }

    // The code of the next sign-in, which the token endpoint redeems once again.
    public void IssueCode()
    {
        lock (_gate)
        {
            _codeUsed = false;
        }
    }

    public void Expire(string refreshToken)
    {
        lock (_gate)
        {
            _refreshTokens.Remove(refreshToken);
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var form = request.Content is null
            ? []
            : (await request.Content.ReadAsStringAsync(cancellationToken)).Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(pair => pair.Split('=', 2))
                .ToDictionary(pair => WebUtility.UrlDecode(pair[0]), pair => WebUtility.UrlDecode(pair[1]), StringComparer.Ordinal);
        var path = request.RequestUri!.AbsolutePath;
        lock (_gate)
        {
            Requests.Add(new AuthorityRequest(path, form));
        }

        switch (path)
        {
            case "/.well-known/openid-configuration" when DiscoveryHangs:
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            case "/.well-known/openid-configuration" when Discovery is { } discovery:
                return Json(HttpStatusCode.OK, discovery);
            case "/.well-known/openid-configuration":
                return Json(HttpStatusCode.OK, new JsonObject
                {
                    ["issuer"] = DiscoveredIssuer,
                    ["authorization_endpoint"] = "https://cms.example.com/connect/authorize",
                    ["token_endpoint"] = "https://cms.example.com/connect/token",
                    ["revocation_endpoint"] = "https://cms.example.com/connect/revoke",
                    ["end_session_endpoint"] = "https://cms.example.com/connect/logout",
                    ["code_challenge_methods_supported"] = new JsonArray("S256"),
                });
            case "/connect/revoke":
                if (HoldRevoke is { } revoke)
                {
                    await revoke.Task.WaitAsync(cancellationToken);
                }

                return new HttpResponseMessage(RevokeStatus);
            case "/connect/token" when form.GetValueOrDefault("grant_type") == "authorization_code":
                lock (_gate)
                {
                    if (form.GetValueOrDefault("code") != Code || _codeUsed)
                    {
                        return Error("invalid_grant");
                    }

                    _codeUsed = true;
                }

                return Tokens(withIdentity: true);
            case "/connect/token" when form.GetValueOrDefault("grant_type") == "refresh_token":
                if (HoldRefresh is { } hold)
                {
                    await hold.Task.WaitAsync(cancellationToken);
                }

                if (RefreshFailsOnTheNetwork)
                {
                    throw new HttpRequestException("The connection was reset.");
                }

                if (RefreshStatus is { } status)
                {
                    return new HttpResponseMessage(status);
                }

                lock (_gate)
                {
                    if (RefreshError is { } error || !_refreshTokens.Remove(form.GetValueOrDefault("refresh_token") ?? ""))
                    {
                        return Error(RefreshError ?? "invalid_grant");
                    }
                }

                return Tokens(withIdentity: false);
            default:
                return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    // The access token holds its number, so a test can tell which one a request carried.
    private HttpResponseMessage Tokens(bool withIdentity)
    {
        string access, refresh;
        lock (_gate)
        {
            _issued++;
            access = $"access-{_issued}" + new string('.', AccessTokenPadding);
            refresh = $"refresh-{_issued}";
            _refreshTokens.Add(refresh);
            (LastAccessToken, LastRefreshToken) = (access, refresh);
        }

        var answer = new JsonObject
        {
            ["access_token"] = access,
            ["token_type"] = "Bearer",
            ["expires_in"] = ExpiresIn,
        };
        if (IssuesRefreshTokens)
        {
            answer["refresh_token"] = refresh;
        }

        if (withIdentity)
        {
            answer["id_token"] = IdToken(new Dictionary<string, object?>
            {
                ["iss"] = Issuer,
                ["aud"] = Audience,
                ["sub"] = Subject,
                ["name"] = "ada",
                ["email"] = "ada@example.com",
                ["nonce"] = Nonce,
                ["exp"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
            });
        }

        return Json(HttpStatusCode.OK, answer);
    }

    private static HttpResponseMessage Error(string error) => Json(HttpStatusCode.BadRequest, new JsonObject { ["error"] = error });

    private static HttpResponseMessage Json(HttpStatusCode status, JsonObject body) =>
        new(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
}

// The reads of the shell's start go to its own fake while the shell is loading, so that a test of the sign-in scripts only
// the requests it makes itself.
internal sealed class ShellReadsAside(HttpMessageHandler shell, HttpMessageHandler api, Func<bool> shellLoading) : HttpMessageHandler
{
    private static readonly string[] ShellReads = ["me", "me/sites", "me/preferences", "me/access", "schema", "plugins"];

    private readonly HttpMessageInvoker _shell = new(shell, disposeHandler: false);
    private readonly HttpMessageInvoker _api = new(api, disposeHandler: false);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        shellLoading() && ShellReads.Contains(request.RequestUri!.AbsolutePath["/api/management/v1/".Length..])
            ? _shell.SendAsync(request, cancellationToken)
            : _api.SendAsync(request, cancellationToken);
}

// A tab that is signed in, for the tests of pages, which are not about the sign-in.
internal sealed class SignedInTestSession : IAdminSession
{
    public SignedInUser? User { get; } = new("0193a1b2-0000-7000-8000-000000000001", "ada", "ada@example.com");

    public event Action? Changed
    {
        add { }
        remove { }
    }

    public Task<bool> EnsureSignedInAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    // The routes the tab was asked to sign in again to come back to.
    public List<string?> SignIns { get; } = [];

    // Thrown by SignInAsync, as the real session throws when the authorization server cannot be reached.
    public Exception? SignInError { get; set; }

    public Task SignInAsync(string? returnRoute, CancellationToken cancellationToken)
    {
        if (SignInError is { } failure)
        {
            return Task.FromException(failure);
        }

        SignIns.Add(returnRoute);
        return Task.CompletedTask;
    }

    public Task<SignInCompletion> CompleteSignInAsync(string? code, string? state, string? error, CancellationToken cancellationToken) =>
        Task.FromResult(SignInCompletion.Failed(SignInFailure.UnknownState));

    public bool SigningOut => false;

    public int SignOuts { get; private set; }

    public Task SignOutAsync(CancellationToken cancellationToken)
    {
        SignOuts++;
        return Task.CompletedTask;
    }
}

// A clock a test moves. Its timers fire only when Advance passes their time.
internal sealed class MovableTime : TimeProvider
{
    private readonly Lock _gate = new();
    private readonly List<ManualTimer> _timers = [];

    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => Now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        lock (_gate)
        {
            _timers.Add(timer);
        }

        return timer;
    }

    public void Advance(TimeSpan by)
    {
        Now += by;
        List<ManualTimer> due;
        lock (_gate)
        {
            due = [.. _timers.Where(timer => timer.DueAt is { } at && at <= Now)];
        }

        foreach (var timer in due)
        {
            timer.Fire();
        }
    }

    private sealed class ManualTimer(MovableTime time, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset? DueAt { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : time.Now + dueTime;
            return true;
        }

        public void Fire()
        {
            DueAt = null;
            callback(state);
        }

        public void Dispose() => DueAt = null;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

// The host's navigation, for a test that builds the services without a renderer.
internal sealed class StubNavigationManager : NavigationManager
{
    public StubNavigationManager() => Initialize("https://admin.example.com/admin/", "https://admin.example.com/admin/");
}

// The host's JavaScript runtime, for a test in which no script runs.
internal sealed class NoJSRuntime : IJSRuntime
{
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
        throw new InvalidOperationException($"No script runs in this test; {identifier} was called.");

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
        throw new InvalidOperationException($"No script runs in this test; {identifier} was called.");
}
