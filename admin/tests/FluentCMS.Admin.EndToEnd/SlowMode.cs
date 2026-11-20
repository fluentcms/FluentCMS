using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Playwright;

namespace FluentCMS.Admin.EndToEnd;

// A browser as slow as a two-core CI runner, where a case that waits for the wrong thing fails while it passes on a developer
// machine. With FLUENTCMS_E2E_SLOW set, every page of a context runs with its CPU throttled by the DevTools protocol
// (FLUENTCMS_E2E_SLOW=1 for the rate of 4, or the rate itself, such as 6) and with added network latency
// (FLUENTCMS_E2E_SLOW_LATENCY, 150 ms by default). Without it nothing changes. The host process is not slowed.
internal sealed class SlowMode
{
    public const string Variable = "FLUENTCMS_E2E_SLOW";
    public const string LatencyVariable = "FLUENTCMS_E2E_SLOW_LATENCY";

    private const double DefaultRate = 4;
    private const double DefaultLatency = 150;

    private readonly IBrowserContext _context;
    private readonly double _rate;
    private readonly double _latency;
    private readonly ConcurrentDictionary<IPage, Task> _pages = new();

    private SlowMode(IBrowserContext context, double rate, double latency)
    {
        (_context, _rate, _latency) = (context, rate, latency);
        context.Page += (_, page) => Throttled(page);
    }

    // The slow mode of the context, or none when the variable is not set.
    public static SlowMode? For(IBrowserContext context)
    {
        var value = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(value) || value is "0" or "false")
        {
            return null;
        }

        var rate = value is "1" or "true" ? DefaultRate : Number(value, Variable, 1);
        var latency = Environment.GetEnvironmentVariable(LatencyVariable) is { Length: > 0 } text ? Number(text, LatencyVariable, 0) : DefaultLatency;
        return new SlowMode(context, rate, latency);
    }

    // Every page of the context, a popup included, is throttled as it opens; a test that opens one waits for it here.
    public Task Throttled(IPage page) => _pages.GetOrAdd(page, ThrottleAsync);

    private async Task ThrottleAsync(IPage page)
    {
        var session = await _context.NewCDPSessionAsync(page);
        await session.SendAsync("Emulation.setCPUThrottlingRate", new Dictionary<string, object> { ["rate"] = _rate });
        await session.SendAsync("Network.enable");
        await session.SendAsync("Network.emulateNetworkConditions", new Dictionary<string, object>
        {
            ["offline"] = false,
            ["latency"] = _latency,
            ["downloadThroughput"] = -1,
            ["uploadThroughput"] = -1,
        });
    }

    private static double Number(string text, string variable, double least) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number >= least
            ? number
            : throw new InvalidOperationException($"{variable} is '{text}'; set it to a number of at least {least}.");
}
