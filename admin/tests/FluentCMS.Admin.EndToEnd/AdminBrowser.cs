using System.Collections.Concurrent;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// One browser context of a test, which its tabs share as a user's browser does: cookies across tabs, sessionStorage per
// tab. With FLUENTCMS_E2E_TRACES set to a directory, the context is traced and a failed test leaves its trace there,
// masked by TraceScrubber; without it nothing is recorded.
public sealed class AdminBrowser : IAsyncDisposable
{
    public const string TracesVariable = "FLUENTCMS_E2E_TRACES";

    // Waits on the page's state are bounded by these, never by a sleep. WebAssembly starts in a few seconds on a runner.
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly EndToEndHost _host;
    private readonly IBrowserContext _context;
    private readonly string? _traces;
    private readonly ConcurrentQueue<string> _violations = new();
    private readonly ConcurrentBag<string> _passwords = [];

    private AdminBrowser(EndToEndHost host, IBrowserContext context, string? traces)
    {
        _host = host;
        _context = context;
        _traces = traces;
    }

    public EndToEndHost Host => _host;

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

    public async Task<AdminTab> NewTabAsync()
    {
        var page = await _context.NewPageAsync();
        return Watch(page);
    }

    // A tab the admin opened itself, as window.open does, which starts with a copy of the opener's sessionStorage the way a
    // duplicated tab does.
    public async Task<AdminTab> OpenedByAsync(AdminTab opener)
    {
        var page = await opener.Page.RunAndWaitForPopupAsync(() => opener.Page.EvaluateAsync("() => { window.open(location.href); }"));
        return Watch(page);
    }

    // The hosted login page, filled in and sent as the editor does.
    public async Task SignInOnTheHostedPageAsync(AdminTab tab, Editor editor)
    {
        _passwords.Add(editor.Password);
        var page = tab.Page;
        await page.WaitForURLAsync(url => new Uri(url).AbsolutePath == "/account/login");
        await page.GetByLabel("User name or email").FillAsync(editor.Email);
        await page.GetByLabel("Password", new PageGetByLabelOptions { Exact = true }).FillAsync(editor.Password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in", Exact = true }).ClickAsync();
    }

    // The trace of this context, masked, at path; the same as a failed test leaves.
    public async Task SaveTraceAsync(string path)
    {
        var raw = path + ".raw";
        await _context.Tracing.StopAsync(new TracingStopOptions { Path = raw });
        try
        {
            TraceScrubber.Scrub(raw, path, [.. _passwords]);
        }
        finally
        {
            File.Delete(raw);
        }
    }

    public IReadOnlyCollection<string> Passwords => [.. _passwords];

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_traces is not null)
            {
                if (TestContext.Current.TestState?.Result == TestResult.Failed)
                {
                    Directory.CreateDirectory(_traces);
                    await SaveTraceAsync(Path.Combine(_traces, TraceName() + ".zip"));
                }
                else
                {
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

    private AdminTab Watch(IPage page)
    {
        page.Console += (_, message) =>
        {
            if (message.Text.Contains("Content Security Policy", StringComparison.OrdinalIgnoreCase))
            {
                _violations.Enqueue(message.Text);
            }
        };
        return new AdminTab(_host, page);
    }
}

// One tab, with what it asked of the authorization server: the address of each authorize request, and the grant of each
// token request, never a token or a code.
public sealed class AdminTab
{
    private readonly EndToEndHost _host;
    private readonly ConcurrentQueue<Uri> _authorizeRequests = new();
    private readonly ConcurrentQueue<string> _loginPages = new();

    internal AdminTab(EndToEndHost host, IPage page)
    {
        _host = host;
        Page = page;
        page.Request += (_, request) =>
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
        };
    }

    public IPage Page { get; }

    public IReadOnlyCollection<Uri> AuthorizeRequests => _authorizeRequests;

    public int LoginPagesShown => _loginPages.Count;

    public ILocator Heading(string resourceKey) =>
        Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = Repository.AdminText(resourceKey), Exact = true });

    public ILocator Button(string resourceKey) =>
        Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = Repository.AdminText(resourceKey), Exact = true });

    public ILocator LoginField => Page.GetByLabel("User name or email");

    // Runs the action and returns the grant of the token request it caused, once the server has answered it.
    public async Task<TokenRequest> TokenRequestOfAsync(Func<Task> action)
    {
        var response = await Page.RunAndWaitForResponseAsync(action, IsTokenRequest, new PageRunAndWaitForResponseOptions { Timeout = (float)AdminBrowser.Wait.TotalMilliseconds });
        return TokenRequest.Of(response);
    }

    public Task<TokenRequest> ReloadAsync() => TokenRequestOfAsync(() => Page.ReloadAsync());

    private bool IsTokenRequest(IResponse response) =>
        response.Request.Method == "POST" && response.Url == new Uri(_host.Origin, "connect/token").AbsoluteUri;
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
