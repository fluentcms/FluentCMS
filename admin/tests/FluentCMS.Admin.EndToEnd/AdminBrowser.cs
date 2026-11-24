using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// One browser context of a test, which its tabs share as a user's browser does: cookies across tabs, sessionStorage per
// tab. It listens to the whole context, so a tab's requests are seen from its first, a tab opened by the page included.
// With FLUENTCMS_E2E_TRACES set to a directory, the context is traced and a failed test leaves its trace there, masked by
// TraceScrubber; without it nothing is recorded. The passwords, tokens, codes and cookies the context saw are kept in
// memory only, for the mask and the check of a trace.
public sealed class AdminBrowser
{
    public const string TracesVariable = "FLUENTCMS_E2E_TRACES";

    // Waits on the page's state are bounded by these, never by a sleep. WebAssembly starts in a few seconds on a runner.
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    // The fields of the sign-in's forms and of the token endpoint's answer whose values are secrets.
    private static readonly string[] SecretFields = ["code", "code_verifier", "refresh_token", "access_token", "id_token", "token", "__RequestVerificationToken"];

    private readonly EndToEndHost _host;
    private readonly IBrowserContext _context;
    private readonly string? _traces;
    private readonly SlowMode? _slow;
    private readonly ConcurrentDictionary<IPage, AdminTab> _tabs = new();
    private readonly ConcurrentQueue<string> _violations = new();
    private readonly ConcurrentDictionary<string, bool> _secrets = new(StringComparer.Ordinal);
    private readonly ConcurrentBag<Task> _reads = [];
    private bool _tracing;

    private AdminBrowser(EndToEndHost host, IBrowserContext context, string? traces)
    {
        _host = host;
        _context = context;
        _traces = traces;
        _tracing = traces is not null;
        _slow = SlowMode.For(context);
        context.Request += (_, request) => OnRequest(request);
        context.Response += (_, response) => OnResponse(response);
        context.Console += (_, message) =>
        {
            if (message.Text.Contains("Content Security Policy", StringComparison.OrdinalIgnoreCase))
            {
                _violations.Enqueue(message.Text);
            }
        };
    }

    // The console messages of a Content-Security-Policy violation, in any tab.
    public IReadOnlyCollection<string> PolicyViolations => _violations;

    internal static async Task<AdminBrowser> OpenAsync(EndToEndHost host, IBrowser browser, string? traces)
    {
        SetDefaultExpectTimeout((float)Wait.TotalMilliseconds);
        var context = await browser.NewContextAsync(new BrowserNewContextOptions { Locale = "en-US", ServiceWorkers = ServiceWorkerPolicy.Block });
        context.SetDefaultTimeout((float)Wait.TotalMilliseconds);
        context.SetDefaultNavigationTimeout((float)Wait.TotalMilliseconds);
        if (traces is not null)
        {
            await context.Tracing.StartAsync(new TracingStartOptions { Screenshots = true, Snapshots = true, Sources = false, Title = TestContext.Current.Test?.TestDisplayName });
        }

        return new AdminBrowser(host, context, traces);
    }

    public async Task<AdminTab> NewTabAsync() => await ThrottledAsync(await _context.NewPageAsync());

    // A tab the admin opened itself, as window.open does, which starts with a copy of the opener's sessionStorage the way a
    // duplicated tab does.
    public async Task<AdminTab> OpenedByAsync(AdminTab opener) =>
        await ThrottledAsync(await opener.Page.RunAndWaitForPopupAsync(() => opener.Page.EvaluateAsync("() => { window.open(location.href); }")));

    // The hosted login page, filled in and sent as the editor does.
    public async Task SignInOnTheHostedPageAsync(AdminTab tab, Editor editor)
    {
        Keep(editor.Password);
        var page = tab.Page;
        // The login page is waited for by its field: a wait for the navigation to its address can start after Playwright has
        // taken that navigation in, and then waits for one that does not come.
        var login = page.GetByLabel("User name or email");
        await login.WaitForAsync(new LocatorWaitForOptions { Timeout = (float)Wait.TotalMilliseconds });
        new Uri(page.Url).AbsolutePath.ShouldBe("/account/login");
        await login.FillAsync(editor.Email);
        try
        {
            await page.GetByLabel("Password", new PageGetByLabelOptions { Exact = true }).FillAsync(editor.Password);
        }
        catch (PlaywrightException exception)
        {
            // Playwright's call log quotes the value it was filling in.
            throw new PlaywrightException(exception.Message.Replace(editor.Password, "[masked]", StringComparison.Ordinal));
        }

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in", Exact = true }).ClickAsync();
    }

    // The trace of this context, masked, at path; the same as a failed test leaves. The unmasked trace is written outside the
    // traces folder, in a folder of the run's host, which is deleted with it if this process dies first.
    public async Task SaveTraceAsync(string path)
    {
        var raw = _host.NewDirectory();
        _tracing = false;
        try
        {
            await _context.Tracing.StopAsync(new TracingStopOptions { Path = Path.Combine(raw.FullName, "trace.zip") });
            await ReadsAsync();
            TraceScrubber.Scrub(Path.Combine(raw.FullName, "trace.zip"), path, [.. _secrets.Keys]);
        }
        finally
        {
            raw.Delete(recursive: true);
        }
    }

    // A secret the test read from the page, as from the clipboard, so that a kept trace is masked of it and checked for it.
    public void Mask(string secret) => Keep(secret);

    // The address of each page of the context, masked, for the output of a failed test.
    internal IEnumerable<string> Addresses => _context.Pages.Select(page => Masked(page.Url));

    // The text with every secret this context has seen replaced.
    internal string Masked(string text) =>
        _secrets.Keys.OrderByDescending(secret => secret.Length).Aggregate(text, (masked, secret) => masked.Replace(secret, "[masked]", StringComparison.Ordinal));

    // Lets the pages of the host read and write the clipboard, as a user who allows it does.
    public Task GrantClipboardAsync() =>
        _context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"], new BrowserContextGrantPermissionsOptions { Origin = _host.Origin.GetLeftPart(UriPartial.Authority) });

    // Every secret this context has seen, for a test of a kept trace.
    public async Task<IReadOnlyCollection<string>> SecretsAsync()
    {
        await ReadsAsync();
        return [.. _secrets.Keys];
    }

    internal async Task CloseAsync(bool failed)
    {
        try
        {
            if (_tracing)
            {
                if (failed)
                {
                    Directory.CreateDirectory(_traces!);
                    await SaveTraceAsync(Path.Combine(_traces!, TraceName() + ".zip"));
                }
                else
                {
                    _tracing = false;
                    await _context.Tracing.StopAsync();
                }
            }
        }
        finally
        {
            await _context.CloseAsync();
        }
    }

    private static string TraceName()
    {
        var name = TestContext.Current.Test?.TestDisplayName ?? "test";
        var invalid = Path.GetInvalidFileNameChars();
        return new string([.. name.Select(character => invalid.Contains(character) || character == ' ' ? '-' : character)]);
    }

    private static IPage? PageOf(IRequest request)
    {
        try
        {
            return request.Frame.Page;
        }
        catch (PlaywrightException)
        {
            // A request of a worker has no frame.
            return null;
        }
    }

    // A tab once the slow mode, when it is on, has throttled its page.
    private async Task<AdminTab> ThrottledAsync(IPage page)
    {
        if (_slow is not null)
        {
            await _slow.Throttled(page);
        }

        return TabOf(page);
    }

    private AdminTab TabOf(IPage page) => _tabs.GetOrAdd(page, created => new AdminTab(_host, created));

    private void OnRequest(IRequest request)
    {
        if (PageOf(request) is { } page)
        {
            TabOf(page).Saw(request);
        }

        var url = new Uri(request.Url);
        KeepFields(HttpUtility.ParseQueryString(url.Query));
        if (request.PostData is { Length: > 0 } form)
        {
            KeepFields(HttpUtility.ParseQueryString(form));
        }

        if (request.Headers.TryGetValue("authorization", out var authorization) && authorization.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            Keep(authorization["Bearer ".Length..]);
        }
    }

    private void OnResponse(IResponse response)
    {
        var tab = PageOf(response.Request) is { } page ? TabOf(page) : null;
        var token = tab is not null && tab.IsTokenRequest(response);
        if (token)
        {
            tab!.Saw(TokenRequest.Of(response));
        }

        if (!response.Url.StartsWith(_host.Origin.AbsoluteUri, StringComparison.Ordinal))
        {
            return;
        }

        _reads.Add(ReadAsync(response, token ? tab : null, IsSecretAnswer(response)));
    }

    // A Management call that may answer a secret shown once, as making or rotating an API key: a POST whose answer is JSON.
    private bool IsSecretAnswer(IResponse response) =>
        response.Request.Method == "POST" && response.Status is 200 or 201
        && response.Url.StartsWith(new Uri(_host.Origin, "api/management/v1/").AbsoluteUri, StringComparison.Ordinal);

    // The cookies a response sets, the tokens of the token endpoint's answer and the secret of a Management answer. A body
    // that can no longer be read, as a redirect's or one Chromium has dropped, is skipped; the access token is also kept from
    // the requests that carry it, and a test keeps a secret it read from the page with Mask.
    private async Task ReadAsync(IResponse response, AdminTab? tab, bool secretAnswer)
    {
        try
        {
            foreach (var cookie in await response.HeaderValuesAsync("set-cookie"))
            {
                var pair = cookie.Split(';', 2)[0].Split('=', 2);
                if (pair.Length == 2)
                {
                    Keep(pair[1]);
                }
            }

            if (tab is not null && response.Status == 200 && JsonNode.Parse(await response.TextAsync()) is JsonObject answer)
            {
                foreach (var field in SecretFields)
                {
                    if (answer[field] is JsonValue value && value.TryGetValue<string>(out var text))
                    {
                        Keep(text);
                    }
                }

            }

            if (secretAnswer && JsonNode.Parse(await response.TextAsync()) is JsonObject created && created["secret"] is JsonValue secret
                && secret.TryGetValue<string>(out var shownOnce))
            {
                Keep(shownOnce);
            }
        }
        catch (Exception exception) when (exception is PlaywrightException or System.Text.Json.JsonException)
        {
        }
    }

    private async Task ReadsAsync()
    {
        foreach (var read in _reads.ToArray())
        {
            await read;
        }
    }

    private void KeepFields(System.Collections.Specialized.NameValueCollection fields)
    {
        foreach (var field in SecretFields.Append("Input.Password"))
        {
            if (fields[field] is { } value)
            {
                Keep(value);
            }
        }
    }

    // A value under 8 characters is not kept: it would mask ordinary text, and no secret of the sign-in is that short.
    private void Keep(string value)
    {
        if (value.Length >= 8)
        {
            _secrets.TryAdd(value, true);
        }
    }
}

// One tab, with what it asked of the authorization server: the address of each authorize request, each login page shown, and
// the grant and status of each token request, never a token or a code.
public sealed class AdminTab
{
    private readonly EndToEndHost _host;
    private readonly ConcurrentQueue<Uri> _authorizeRequests = new();
    private readonly ConcurrentQueue<string> _loginPages = new();
    private readonly ConcurrentQueue<TokenRequest> _tokenRequests = new();
    private long _lastGrant;

    internal AdminTab(EndToEndHost host, IPage page)
    {
        _host = host;
        Page = page;
    }

    public IPage Page { get; }

    public IReadOnlyCollection<Uri> AuthorizeRequests => _authorizeRequests;

    public int LoginPagesShown => _loginPages.Count;

    public IReadOnlyCollection<TokenRequest> TokenRequests => _tokenRequests;

    public ILocator Heading(string resourceKey) =>
        Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = Repository.AdminText(resourceKey), Exact = true });

    // A button of the wrapped set. Fluent UI's fluent-button takes its role from ElementInternals, which Chromium's
    // accessibility tree has but Playwright's role selector does not read (it reads the role attribute and the tag), so it is
    // found by its element and its whole text, from the resources.
    public ILocator Button(string resourceKey) =>
        Page.Locator("fluent-button").Filter(new LocatorFilterOptions { HasTextRegex = new Regex($@"^\s*{Regex.Escape(Repository.AdminText(resourceKey))}\s*$") });

    public ILocator LoginField => Page.GetByLabel("User name or email");

    // The sidebar, a navigation landmark named by the resources.
    public ILocator Sidebar => Page.GetByRole(AriaRole.Navigation, new PageGetByRoleOptions { Name = Repository.AdminText("Shell.Sidebar.Label"), Exact = true });

    public ILocator SiteSwitcher => Page.GetByLabel(Repository.AdminText("Shell.SiteSwitcher.Label"), new PageGetByLabelOptions { Exact = true });

    // What the live region has announced, which a screen reader reads. It keeps its last few messages, so the same text can
    // be there more than once.
    public ILocator Announced(string text) => Page.GetByRole(AriaRole.Status).GetByText(text, new LocatorGetByTextOptions { Exact = true }).Last;

    // Runs the action and returns the grant of the token request it caused, once the server has answered it.
    public async Task<TokenRequest> TokenRequestOfAsync(Func<Task> action)
    {
        var response = await Page.RunAndWaitForResponseAsync(action, IsTokenRequest, new PageRunAndWaitForResponseOptions { Timeout = (float)AdminBrowser.Wait.TotalMilliseconds });
        return TokenRequest.Of(response);
    }

    public Task<TokenRequest> ReloadAsync() => TokenRequestOfAsync(() => Page.ReloadAsync());

    // Chooses the site and returns the answer of /me/access, the first read of a switch, once it has come: a 401 is followed
    // by one refresh and the call again, and that answer is the one returned. A read of the site before, such as the shell's
    // first one on a slow machine, may still be answered after the choice, so the read is the one that names the site.
    public async Task<IResponse> SwitchSiteAsync(Site site)
    {
        var access = await this.AnswerAsync(
            () => SiteSwitcher.SelectOptionAsync(new SelectOptionValue { Value = site.Id }),
            response => response.Url == ManagementUrl("me/access") && string.Equals(response.Request.Headers.GetValueOrDefault("x-cms-site"), site.Id, StringComparison.OrdinalIgnoreCase));
        await Expect(Announced(Repository.AdminText("Shell.SiteSwitcher.Switched").Replace("{0}", site.Name, StringComparison.Ordinal))).ToBeAttachedAsync();
        return access;
    }

    public string ManagementUrl(string path) => new Uri(_host.Origin, "api/management/v1/" + path).AbsoluteUri;

    internal bool IsTokenRequest(IResponse response) =>
        response.Request.Method == "POST" && response.Url == new Uri(_host.Origin, "connect/token").AbsoluteUri;

    internal void Saw(IRequest request)
    {
        var url = new Uri(request.Url);
        if (url.AbsolutePath == "/connect/authorize")
        {
            _authorizeRequests.Enqueue(url);
        }
        else if (url.AbsolutePath == "/account/login" && request.Method == "GET")
        {
            _loginPages.Enqueue(url.AbsolutePath);
        }
    }

    internal void Saw(TokenRequest request)
    {
        if (request.Status == 200)
        {
            Interlocked.Exchange(ref _lastGrant, DateTime.UtcNow.Ticks);
        }

        _tokenRequests.Enqueue(request);
    }

    // Returns once the tab's access token is due for its refresh on demand, half its lifetime after the last grant, so that
    // the tab's next call refreshes it. The token's age is waited for; no request is sent meanwhile.
    public async Task TokenDueAsync()
    {
        var granted = Interlocked.Read(ref _lastGrant);
        granted.ShouldBeGreaterThan(0, "the tab has no token yet");
        var due = new DateTime(granted, DateTimeKind.Utc) + TimeSpan.Parse(EndToEndHost.AccessTokenLifetime, System.Globalization.CultureInfo.InvariantCulture) / 2 + TimeSpan.FromMilliseconds(250);
        var wait = due - DateTime.UtcNow;
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, TestContext.Current.CancellationToken);
        }
    }
}

// The names of a token request's form fields, with the value of grant_type only, and the status of its answer.
public sealed record TokenRequest(int Status, string? GrantType, IReadOnlySet<string> Fields)
{
    internal static TokenRequest Of(IResponse response)
    {
        var form = (response.Request.PostData ?? "").Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => pair.Length > 1 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : "", StringComparer.Ordinal);
        return new TokenRequest(response.Status, form.GetValueOrDefault("grant_type"), form.Keys.ToHashSet(StringComparer.Ordinal));
    }

    public void ShouldBeGranted(string grantType)
    {
        GrantType.ShouldBe(grantType);
        Status.ShouldBe(200, $"the {grantType} grant was refused");
    }
}
