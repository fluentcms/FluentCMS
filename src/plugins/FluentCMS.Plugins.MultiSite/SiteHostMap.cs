using System.Collections.Frozen;
using FluentCMS.Events;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Plugins.MultiSite;

// The site of every host name, which core's site resolver asks for a request that names no site (spec 4.6). The rows of
// cms_multi_site_host are read whole and kept for every request of this instance for CacheDuration; a write through
// SiteHostService drops them at once, and another instance sees it when its copy expires, as core's SiteCache does. The map
// is the instance's and is not gated per site, since a host name is read before any site is known. A read that fails fails
// the request: answering with the default site would serve one site's host name another site's content. The requests that
// find the copy expired or dropped at once share one read of the rows.
internal sealed class SiteHostMap(IServiceScopeFactory scopes, TimeProvider time) : ISiteHostResolver
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private readonly Lock _gate = new();
    private Snapshot? _snapshot;
    private Task<Snapshot>? _reading;
    private long _readingGeneration;
    private long _generation;

    public async ValueTask<SiteId?> ResolveAsync(string host, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);

        // A host sent in the stored form, as most are, is found as it is, and only another is put in that form.
        var sites = await CurrentAsync(cancellationToken);
        return sites.TryGetValue(host, out var site) || (HostNames.OfRequest(host) is { } name && sites.TryGetValue(name, out site))
            ? new SiteId(site)
            : null;
    }

    public void Invalidate() => Interlocked.Increment(ref _generation);

    private async ValueTask<FrozenDictionary<string, Guid>> CurrentAsync(CancellationToken cancellationToken)
    {
        var generation = Interlocked.Read(ref _generation);
        if (Volatile.Read(ref _snapshot) is { } snapshot && snapshot.Generation == generation && snapshot.Expires > time.GetUtcNow())
        {
            return snapshot.Sites;
        }

        // One read for every request that finds the copy out of date at once, on a scope of its own and with no request's
        // token, so that one that goes away does not fail the others. A read begun before a write is not shared after it.
        Task<Snapshot> reading;
        lock (_gate)
        {
            if (_reading is null || _reading.IsCompleted || _readingGeneration != generation)
            {
                _readingGeneration = generation;
                _reading = ReadAsync(generation);
            }

            reading = _reading;
        }

        return (await reading.WaitAsync(cancellationToken)).Sites;
    }

    private async Task<Snapshot> ReadAsync(long generation)
    {
        await Task.Yield();
        await using var scope = scopes.CreateAsyncScope();
        var rows = await scope.ServiceProvider.GetRequiredService<PluginDbContext<MultiSitePlugin>>().Set<SiteHost>().AsNoTracking()
            .Select(host => new { host.Hostname, host.SiteId })
            .ToListAsync(CancellationToken.None);
        var sites = rows.ToFrozenDictionary(row => row.Hostname, row => row.SiteId, StringComparer.Ordinal);

        // A write during the read leaves this copy a generation behind, so the next reader reads again.
        var snapshot = new Snapshot(sites, generation, time.GetUtcNow() + CacheDuration);
        Volatile.Write(ref _snapshot, snapshot);
        return snapshot;
    }

    private sealed record Snapshot(FrozenDictionary<string, Guid> Sites, long Generation, DateTimeOffset Expires);
}

// The hosts of a site go with it. The site's own rows are core's, which deletes them; these are the plugin's. An outbox
// delivery, as the other plugins' cleanups are, so that a removal that fails or is cut off is tried again rather than left.
internal sealed class DeletedSiteHosts(SiteHostService hosts) : IOutboxEventHandler<SiteDeletedEvent>
{
    public async ValueTask HandleAsync(SiteDeletedEvent message, EventDelivery delivery, CancellationToken cancellationToken) =>
        await hosts.RemoveAllAsync(message.SiteId, cancellationToken);
}
