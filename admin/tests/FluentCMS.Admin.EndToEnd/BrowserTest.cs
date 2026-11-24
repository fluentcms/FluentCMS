using Xunit;

namespace FluentCMS.Admin.EndToEnd;

// A class of browser tests. Its browser contexts are closed when the test class is disposed, once xUnit.net knows the
// test's result, so that a failed test leaves its trace when traces are on.
//
// The host is shared by every class, so a case reads only what it wrote (#3314): it signs in as its class's own editor, or
// a user it makes, and names what it makes with its Mark, which is new for each case and each run of it.
[Collection(BrowserTests.Name)]
public abstract class BrowserTest(EndToEndHost host) : IAsyncDisposable
{
    private readonly List<AdminBrowser> _browsers = [];

    protected EndToEndHost Host { get; } = host.Running();

    // A mark of this case alone, for the names, titles and files it makes; xUnit.net makes a new instance for each case.
    protected string Mark { get; } = "e" + Guid.NewGuid().ToString("N")[..8];

    public async ValueTask DisposeAsync()
    {
        var failed = TestContext.Current.TestState?.Result == TestResult.Failed;
        if (failed)
        {
            WriteWhereItStopped();
        }

        foreach (var browser in _browsers)
        {
            await browser.CloseAsync(failed);
        }

        GC.SuppressFinalize(this);
    }

    // The output of a failed case: the address of each page its browsers had open and the last lines of the host's log, which
    // nothing keeps once the run ends. Both are masked of the secrets the browsers saw.
    private void WriteWhereItStopped()
    {
        if (TestContext.Current.TestOutputHelper is not { } output)
        {
            return;
        }

        foreach (var browser in _browsers)
        {
            foreach (var address in browser.Addresses)
            {
                output.WriteLine($"A page was at {address}");
            }
        }

        var log = _browsers.Aggregate(Host.HostLog, (text, browser) => browser.Masked(text));
        output.WriteLine($"The host's log, its last lines:{Environment.NewLine}{log}");
    }

    // The editor of this class, of the built-in role Editor, made the first time a case of the class asks for it. No other
    // class signs in as it, so nothing another class does to its sessions or its data reaches it.
    protected Task<Editor> EditorAsync() => Host.EditorOfAsync(GetType());

    // A browser context of its own; skipped, with the reason, when Chromium is not installed.
    protected async Task<AdminBrowser> OpenBrowserAsync(string? traces = null)
    {
        var browser = await Host.OpenBrowserAsync(traces);
        _browsers.Add(browser);
        return browser;
    }

    // A new tab at the admin's path, signed in on the hosted page as the user, or the class's editor.
    protected async Task<AdminTab> SignedInTabAsync(AdminBrowser browser, string path, Editor? user = null)
    {
        var tab = await browser.NewTabAsync();
        await tab.Page.GotoAsync(new Uri(Host.Admin, path).AbsoluteUri);
        await browser.SignInOnTheHostedPageAsync(tab, user ?? await EditorAsync());
        return tab;
    }
}

// One host serves every test, and the browser tests change its users and sessions, so they run one after another.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BrowserTests
{
    public const string Name = "browser";
}
