using System.Text.Json;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// A signed-in tab keeps its requests while the host runs its commands, which on the image are one-off containers, each adding
// and removing a network interface on the machine the engine runs on (#3635). The tab fetches the largest file of the admin
// that the page loaded, one fetch after another with no pause, so that a request is in flight most of the time, while the host
// runs several commands in a row; a request the browser gives up fails the case, with the browser's error.
public sealed class RequestsInFlightTests(EndToEndHost host) : BrowserTest(host)
{
    // On the image each command adds an interface and removes it. One took about 9 seconds on a loaded machine, and the case
    // keeps under 30.
    private const int Commands = 2;

    private const string LargestFile = """
        () => performance.getEntriesByType('resource')
            .filter(entry => entry.name.startsWith(location.origin + '/') && entry.decodedBodySize > 0)
            .sort((first, second) => second.decodedBodySize - first.decodedBodySize)
            .map(entry => ({ url: entry.name, size: entry.decodedBodySize }))[0] ?? null
        """;

    private const string KeepFetching = """
        async url => {
            window.fluentcmsKeepFetching = true;
            let fetched = 0, refused = 0, failed = 0;            while (window.fluentcmsKeepFetching) {
                try {
                    const answer = await fetch(url, { cache: 'no-store' });
                    await answer.arrayBuffer();
                    answer.ok ? fetched++ : refused++;
                } catch {
                    failed++;
                }
            }
            return { fetched, refused, failed };
        }
        """;

    [Fact]
    public async Task ASignedInTabKeepsItsRequestsInFlightWhileTheHostRunsCommands()
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
            for (var command = 0; command < Commands; command++)
            {
                await Host.CreateEditorAsync();
            }
        }
        finally
        {
            await tab.Page.EvaluateAsync("() => { window.fluentcmsKeepFetching = false; }");
        }

        var counts = await fetching;
        var (fetched, refused, failed) = (counts.GetProperty("fetched").GetInt32(), counts.GetProperty("refused").GetInt32(), counts.GetProperty("failed").GetInt32());
        TestContext.Current.TestOutputHelper?.WriteLine($"{fetched} fetches of {url} ({size} B) while the host ran {Commands} commands");

        var givenUp = browser.RequestsGivenUp.Select(browser.Masked).ToList();
        givenUp.ShouldBeEmpty($"the browser gave up {givenUp.Count} request(s) while the host ran its commands:{Environment.NewLine}{string.Join(Environment.NewLine, givenUp)}");
        failed.ShouldBe(0, "fetches failed with no request the browser gave up");
        refused.ShouldBe(0, "fetches of the file were not answered 200");
        fetched.ShouldBeGreaterThan(Commands);
    }
}
