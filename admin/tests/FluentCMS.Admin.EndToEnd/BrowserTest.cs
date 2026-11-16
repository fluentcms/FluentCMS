using Xunit;

namespace FluentCMS.Admin.EndToEnd;

// A class of browser tests. Its browser contexts are closed when the test class is disposed, once xUnit.net knows the
// test's result, so that a failed test leaves its trace when traces are on.
[Collection(BrowserTests.Name)]
public abstract class BrowserTest(EndToEndHost host) : IAsyncDisposable
{
    private readonly List<AdminBrowser> _browsers = [];

    protected EndToEndHost Host => host;

    public async ValueTask DisposeAsync()
    {
        var failed = TestContext.Current.TestState?.Result == TestResult.Failed;
        foreach (var browser in _browsers)
        {
            await browser.CloseAsync(failed);
        }

        GC.SuppressFinalize(this);
    }

    // A browser context of its own; skipped, with the reason, when Chromium is not installed.
    protected async Task<AdminBrowser> OpenBrowserAsync(string? traces = null)
    {
        var browser = await host.OpenBrowserAsync(traces);
        _browsers.Add(browser);
        return browser;
    }
}

// One host serves every test, and the browser tests change its users and sessions, so they run one after another.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BrowserTests
{
    public const string Name = "browser";
}
