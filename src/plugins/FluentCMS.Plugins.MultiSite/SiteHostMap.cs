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
// the request: answering with the default site would serve one site's host name another site's content.
internal sealed class SiteHostMap(IServiceScopeFactory scopes, TimeProvider time) : ISiteHostResolver
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private Snapshot? _snapshot;
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

        await using var scope = scopes.CreateAsyncScope();
        var rows = await scope.ServiceProvider.GetRequiredService<PluginDbContext<MultiSitePlugin>>().Set<SiteHost>().AsNoTracking()
            .Select(host => new { host.Hostname, host.SiteId })
            .ToListAsync(cancellationToken);
        var sites = rows.ToFrozenDictionary(row => row.Hostname, row => row.SiteId, StringComparer.Ordinal);

        // A write during the read leaves this copy a generation behind, so the next reader reads again.
        Volatile.Write(ref _snapshot, new Snapshot(sites, generation, time.GetUtcNow() + CacheDuration));
        return sites;
    }

    private sealed record Snapshot(FrozenDictionary<string, Guid> Sites, long Generation, DateTimeOffset Expires);
}

// The hosts of a site go with it. The site's own rows are core's, which deletes them; these are the plugin's.
internal sealed class DeletedSiteHosts(SiteHostService hosts) : IEventHandler<SiteDeletedEvent>
{
    public async ValueTask HandleAsync(SiteDeletedEvent message, CancellationToken cancellationToken) =>
        await hosts.RemoveAllAsync(message.SiteId, cancellationToken);
}
