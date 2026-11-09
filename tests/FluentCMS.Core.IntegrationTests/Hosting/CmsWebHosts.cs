using Microsoft.AspNetCore.Builder;

namespace FluentCMS.Core.IntegrationTests.Hosting;

// The started hosts the tests of a class share, one for each set-up, so that the tests that only read what a set-up does pay
// for its start once. A test that changes what a host holds, such as the buckets of a rate limit, builds a host of its own.
// The tests of a class run one at a time, so a set-up starts once; one that fails to start is not kept.
public sealed class CmsWebHosts : IAsyncDisposable
{
    private readonly Dictionary<string, WebApplication> _started = [];
    private readonly List<CmsWebHost> _hosts = [];

    // A set-up named by its environment, its settings in any order, and whatever else the class builds into it.
    public static string SetUp(string environment, IReadOnlyDictionary<string, string?>? settings, params object?[] more) =>
        string.Join('\n', [environment, .. (settings ?? new Dictionary<string, string?>()).OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}"), .. more.Select(item => $"{item}")]);

    public async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            await host.DisposeAsync();
        }
    }

    internal async Task<WebApplication> StartAsync(string setUp, Func<CmsWebHost, Task<WebApplication>> start)
    {
        if (!_started.TryGetValue(setUp, out var app))
        {
            var host = new CmsWebHost();
            _hosts.Add(host);
            app = await start(host);
            _started[setUp] = app;
        }

        return app;
    }
}
