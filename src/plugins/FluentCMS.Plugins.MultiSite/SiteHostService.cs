using FluentCMS.Activity;
using FluentCMS.Authorization;
using FluentCMS.Content;
using FluentCMS.Events;
using FluentCMS.Providers;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins.MultiSite;

public sealed record SiteCreateRequest(string? Name, string? Slug, string? DefaultLocale);

public sealed record SiteHostRequest(string? Hostname, bool? IsPrimary);

public sealed record SiteHostChange(bool? IsPrimary);

public sealed record SiteHostInfo(Guid SiteId, string Hostname, bool IsPrimary, DateTimeOffset CreatedAt);

// The sites beyond the default one and the host names of every site. Creating and deleting a site is core's, through
// ISiteAdministration, which checks sites.manage for the instance and raises the events. The host names need the same
// permission, for the instance and not for the site: they are one namespace, and a host name decides which site every
// request on it reads. A site with hosts has exactly one primary host, the first it got until another is made primary, and
// the primary host is removed last. The writes of host names run one at a time on every instance together, under a lock,
// since no index can hold a site to one primary host on every database.
internal sealed class SiteHostService(
    PluginDbContext<MultiSitePlugin> database,
    ISiteAdministration sites,
    IPermissionChecker permissions,
    IOperationContextAccessor operations,
    IDistributedLock locks,
    IEventBus events,
    IActivityLog activity,
    SiteHostMap map,
    TimeProvider time,
    ILogger<SiteHostService> logger)
{
    public const string ManageSites = "sites.manage";

    public const string LockName = "fluentcms:multi-site-hosts";

    // Core's code for a site that does not exist.
    private const string SiteNotFound = "FCMS2301";

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(45);

    // A member the body leaves out is refused by core as an empty one is.
    public Task<SiteInfo> CreateSiteAsync(SiteCreateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return sites.CreateAsync(request.Name ?? "", request.Slug ?? "", request.DefaultLocale ?? "", cancellationToken);
    }

    // The hosts of the site go with it: DeletedSiteHosts hears the event core raises.
    public Task DeleteSiteAsync(Guid siteId, CancellationToken cancellationToken) => sites.DeleteAsync(new SiteId(siteId), cancellationToken);

    // The primary host first.
    public async Task<IReadOnlyList<SiteHostInfo>> ListAsync(Guid siteId, CancellationToken cancellationToken)
    {
        var site = await SiteAsync(siteId, cancellationToken);
        var hosts = await database.Set<SiteHost>().AsNoTracking().Where(host => host.SiteId == site.Id).ToListAsync(cancellationToken);
        return [.. hosts.OrderByDescending(host => host.IsPrimary).ThenBy(host => host.Hostname, StringComparer.Ordinal).Select(Info)];
    }

    // The first host of a site is its primary host, whatever the request says, and a later one when the request asks for it.
    public async Task<SiteHostInfo> AddAsync(Guid siteId, SiteHostRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var site = await SiteAsync(siteId, cancellationToken);
        var name = HostNames.Parse(request.Hostname, "hostname");

        SiteHost added;
        await using (await LockAsync(cancellationToken))
        {
            if (await database.Set<SiteHost>().AsNoTracking().FirstOrDefaultAsync(host => host.Hostname == name, cancellationToken) is { } taken)
            {
                await ReleaseOrRefuseAsync(taken, site, cancellationToken);
            }

            var others = await database.Set<SiteHost>().Where(host => host.SiteId == site.Id).ToListAsync(cancellationToken);
            added = new SiteHost { SiteId = site.Id, Hostname = name, IsPrimary = others.Count == 0 || request.IsPrimary == true, CreatedAt = time.GetUtcNow() };
            if (added.IsPrimary)
            {
                others.ForEach(other => other.IsPrimary = false);
            }

            database.Add(added);
            await database.SaveChangesAsync(cancellationToken);
            map.Invalidate();
        }

        MultiSiteLog.HostAdded(logger, name, site.Id);
        await ChangedAsync("site.host-added", site, name);
        return Info(added);
    }

    // isPrimary: true makes the host the primary one in place of the one that was. The primary host stays primary until
    // another is made primary, so false is refused for it, and a change that changes nothing raises nothing.
    public async Task<SiteHostInfo> UpdateAsync(Guid siteId, string hostname, SiteHostChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hostname);
        ArgumentNullException.ThrowIfNull(change);

        var site = await SiteAsync(siteId, cancellationToken);

        SiteHost host;
        await using (await LockAsync(cancellationToken))
        {
            var hosts = await database.Set<SiteHost>().Where(item => item.SiteId == site.Id).ToListAsync(cancellationToken);
            host = Find(hosts, hostname);
            if (change.IsPrimary is not { } primary || primary == host.IsPrimary)
            {
                return Info(host);
            }

            if (!primary)
            {
                throw new ValidationException([new ValidationError("isPrimary", MultiSiteErrorCodes.PrimaryHostNeeded, $"'{host.Hostname}' is the primary host of the site; make another host primary instead.")]);
            }

            hosts.ForEach(item => item.IsPrimary = item == host);
            await database.SaveChangesAsync(cancellationToken);
            map.Invalidate();
        }

        MultiSiteLog.HostMadePrimary(logger, host.Hostname, site.Id);
        await ChangedAsync("site.host-updated", site, host.Hostname);
        return Info(host);
    }

    public async Task RemoveAsync(Guid siteId, string hostname, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hostname);

        var site = await SiteAsync(siteId, cancellationToken);

        SiteHost host;
        await using (await LockAsync(cancellationToken))
        {
            var hosts = await database.Set<SiteHost>().Where(item => item.SiteId == site.Id).ToListAsync(cancellationToken);
            host = Find(hosts, hostname);
            if (host.IsPrimary && hosts.Count > 1)
            {
                throw new PluginException(MultiSiteErrorCodes.PrimaryHostNeeded, $"'{host.Hostname}' is the primary host of the site, which is removed last; make another host primary first.");
            }

            database.Remove(host);
            await database.SaveChangesAsync(cancellationToken);
            map.Invalidate();
        }

        MultiSiteLog.HostRemoved(logger, host.Hostname, site.Id);
        await ChangedAsync("site.host-removed", site, host.Hostname);
    }

    // For a site that was deleted, so there is no site left to check or to raise an event for.
    public async Task RemoveAllAsync(Guid siteId, CancellationToken cancellationToken)
    {
        var removed = await database.Set<SiteHost>().Where(host => host.SiteId == siteId).ExecuteDeleteAsync(cancellationToken);
        map.Invalidate();
        if (removed > 0)
        {
            MultiSiteLog.HostsOfDeletedSiteRemoved(logger, removed, siteId);
        }
    }

    // A name that a site which no longer exists still holds is free: its rows were left behind when the site was deleted.
    private async Task ReleaseOrRefuseAsync(SiteHost taken, SiteInfo site, CancellationToken cancellationToken)
    {
        if (taken.SiteId == site.Id)
        {
            throw Taken($"The site has the host name '{taken.Hostname}' already.");
        }

        if (await sites.FindAsync(new SiteId(taken.SiteId), cancellationToken) is not null)
        {
            throw Taken($"Another site has the host name '{taken.Hostname}'.");
        }

        await database.Set<SiteHost>().Where(host => host.Hostname == taken.Hostname && host.SiteId == taken.SiteId).ExecuteDeleteAsync(cancellationToken);
        MultiSiteLog.OrphanHostReleased(logger, taken.Hostname, taken.SiteId);
    }

    private async Task<SiteInfo> SiteAsync(Guid siteId, CancellationToken cancellationToken)
    {
        await permissions.RequireAsync(ManageSites, null, cancellationToken);
        return await sites.FindAsync(new SiteId(siteId), cancellationToken)
            ?? throw new ValidationException([new ValidationError("site", SiteNotFound, $"There is no site {siteId}.")]);
    }

    // Another write of host names holds the lock for longer than the wait: nothing was changed, and the caller tries again.
    private async Task<IDistributedLockHandle> LockAsync(CancellationToken cancellationToken) =>
        await locks.TryAcquireAsync(LockName, Wait, cancellationToken)
            ?? throw new PluginException(MultiSiteErrorCodes.HostNamesBusy, $"Another change to the host names did not finish within {Wait.TotalSeconds:0} seconds, so this one was not made; try again.");

    // A host change is a change to its site: SiteUpdatedEvent drops what core keeps of the site and its Delivery copies.
    private async Task ChangedAsync(string action, SiteInfo site, string hostname)
    {
        var operation = operations.Current ?? throw new InvalidOperationException("The host names of a site are managed inside an operation.");
        var source = EventSource.From(operation) with { SiteId = site.Id };
        await events.PublishAsync(new SiteUpdatedEvent(site.Id, site.Slug) { Source = source }, CancellationToken.None);
        await activity.WriteAsync(
            new ActivityEntry(action)
            {
                TargetType = "site",
                TargetId = site.Id.ToString("D"),
                TargetLabel = site.Slug,
                Details = new Dictionary<string, string?> { ["hostname"] = hostname },
            },
            source,
            CancellationToken.None);
    }

    // The name in the route is looked up as a request's host is, so its case does not matter.
    private static SiteHost Find(List<SiteHost> hosts, string hostname) =>
        (HostNames.OfRequest(hostname) is { } name ? hosts.Find(host => host.Hostname == name) : null)
            ?? throw new PluginException(MultiSiteErrorCodes.HostNotFound, $"The site has no host name '{hostname}'.");

    private static ValidationException Taken(string message) => new([new ValidationError("hostname", MultiSiteErrorCodes.HostNameTaken, message)]);

    private static SiteHostInfo Info(SiteHost host) => new(host.SiteId, host.Hostname, host.IsPrimary, host.CreatedAt);
}
