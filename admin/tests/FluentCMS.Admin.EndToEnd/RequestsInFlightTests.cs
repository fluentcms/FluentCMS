using System.Text.Json;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// A signed-in tab keeps its requests while the host runs a command and after it (#3635). On the image a command is a one-off
// container, and one that adds a network interface to the machine the engine runs on and removes it as it ends makes Chromium
// on Linux close its connections about two seconds after the change. The tab fetches the largest file of the admin that the
// page loaded, one fetch after another with no pause, while the host makes a user with its command and for five seconds
// after the command has ended; a request the browser gives up fails the case, with the browser's error.
public sealed class RequestsInFlightTests(EndToEndHost host) : BrowserTest(host)
{
    private const int AfterCommandMilliseconds = 5000;

    private const string LargestFile = """
        () => performance.getEntriesByType('resource')
            .filter(entry => entry.name.startsWith(location.origin + '/') && entry.decodedBodySize > 0)
            .sort((first, second) => second.decodedBodySize - first.decodedBodySize)
            .map(entry => ({ url: entry.name, size: entry.decodedBodySize }))[0] ?? null
        """;

    // Fetches until the time the case sets once the command has ended; a fetch that hangs is given up after 30 seconds, so
    // that the case fails rather than waits for the run's timeout.
    private const string KeepFetching = """
        async url => {
            window.fluentcmsFetchUntil = Infinity;
            let fetched = 0, afterCommand = 0, refused = 0, failed = 0;
            while (performance.now() < window.fluentcmsFetchUntil) {
                const after = window.fluentcmsFetchUntil !== Infinity;
                try {
                    const answer = await fetch(url, { cache: 'no-store', signal: AbortSignal.timeout(30000) });
                    await answer.arrayBuffer();
                    answer.ok ? fetched++ : refused++;
                    afterCommand += after ? 1 : 0;
                } catch {
                    failed++;
                }
            }
            return { fetched, afterCommand, refused, failed };
        }
        """;

    private const string StopFetchingAfter = "milliseconds => { window.fluentcmsFetchUntil = performance.now() + milliseconds; }";

    [Fact]
    public async Task ASignedInTabKeepsItsRequestsWhileTheHostRunsACommandAndAfterIt()
    {
        var browser = await OpenBrowserAsync();
        var tab = await SignedInTabAsync(browser, "");
        await Expect(tab.Sidebar).ToBeVisibleAsync();

        var file = await tab.Page.EvaluateAsync<JsonElement>(LargestFile);
        file.ValueKind.ShouldBe(JsonValueKind.Object, "the page loaded no file of the host");
        var url = file.GetProperty("url").GetString()!;
        var size = file.GetProperty("size").GetInt64();
        size.ShouldBeGreaterThan(100_000, $"the largest file the page loaded, {url}, is too small to keep a request in flight");

        var fetching = tab.Page.EvaluateAsync<JsonElement>(KeepFetching, url);
        try
        {
            await Host.CreateEditorAsync();
        }
        finally
        {
            await tab.Page.EvaluateAsync(StopFetchingAfter, AfterCommandMilliseconds);
        }

        var counts = await fetching;
        int Count(string name) => counts.GetProperty(name).GetInt32();
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{Count("fetched")} fetches of {url} ({size} B), {Count("afterCommand")} of them in the {AfterCommandMilliseconds} ms after the command");

        var givenUp = browser.RequestsGivenUp.Select(browser.Masked).ToList();
        givenUp.ShouldBeEmpty($"the browser gave up {givenUp.Count} request(s) while the host ran a command or after it:{Environment.NewLine}{string.Join(Environment.NewLine, givenUp)}");
        Count("failed").ShouldBe(0, "fetches failed with no request the browser gave up");
        Count("refused").ShouldBe(0, "fetches of the file were not answered 200");
        Count("afterCommand").ShouldBeGreaterThan(0, "no fetch ended after the command");
    }
}
