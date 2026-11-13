using FluentCMS.Content;
using FluentCMS.Diagnostics;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluentCMS.Plugins.Media;

// FluentCms:Media:Library: whether the sites of an instance share one media library or keep one each.
public enum MediaLibrary
{
    // Every site reads and writes the library of the default site.
    Shared,

    // Each site has its own assets, folders and presets.
    PerSite,
}

// The site whose assets, folders and presets a request reads and writes: the default site's when the library is shared,
// its own otherwise. Only the rows follow the library. The permissions, with their folder limits, and the media settings
// are those of the site of the request, so each site's roles decide who uploads and who reads.
internal sealed class MediaLibraries(IOptions<MediaOptions> options, ISiteResolver sites)
{
    public async ValueTask<SiteId> OfAsync(SiteId site, CancellationToken cancellationToken) =>
        options.Value.Library == MediaLibrary.PerSite ? site : await sites.ResolveAsync(new SiteRequest(), cancellationToken);
}

// With Shared, the assets a site kept of its own while the library was PerSite are no longer found, and entries that refer
// to them show missing media, though no row moved. The startup report names the option and up to MaxNamed such sites. The
// check never fails the report: what it cannot read is logged and left out.
internal sealed class SharedLibraryReport(IServiceScopeFactory scopes, IOptions<MediaOptions> options, ILogger<SharedLibraryReport> logger) : IInsecureDefaultSource
{
    public const string HiddenSites = "media-shared-library-hides-sites";
    public const int MaxNamed = 5;

    public IEnumerable<InsecureDefault> GetInsecureDefaults()
    {
        if (options.Value.Library != MediaLibrary.Shared)
        {
            return [];
        }

        // The report asks its sources synchronously.
        return FindAsync().GetAwaiter().GetResult() is { } found ? [found] : [];
    }

    // The sites other than the library with an asset that is not deleted, at most one more than are named. The union of the
    // two ranges around the library is read from the index on site_id, where <> is a scan on SQLite and PostgreSQL, and so is
    // an OR of the ranges on SQLite. So the library's own rows are never read, however many, only those of other sites, which
    // a shared library never writes.
    internal static IQueryable<Guid> OtherSites(IQueryable<Asset> assets, Guid library)
    {
        var live = assets.AsNoTracking().Where(asset => asset.DeletedAt == null);
        return live.Where(asset => asset.SiteId.CompareTo(library) < 0).Select(asset => asset.SiteId)
            .Union(live.Where(asset => asset.SiteId.CompareTo(library) > 0).Select(asset => asset.SiteId))
            .Take(MaxNamed + 1);
    }

    private async Task<InsecureDefault?> FindAsync()
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var library = await services.GetRequiredService<ISiteResolver>().ResolveAsync(new SiteRequest());
            var others = await OtherSites(services.GetRequiredService<PluginDbContext<MediaPlugin>>().Set<Asset>(), library.Value).ToListAsync();
            if (others.Count == 0)
            {
                return null;
            }

            // Reading a site needs sites.manage, so the names are read as the system, as an outbox handler would read them.
            var sites = services.GetRequiredService<ISiteAdministration>();
            List<SiteInfo> named = [];
            using (SystemOperation.Begin(services.GetRequiredService<IOperationContextAccessor>(), Origin.Job, library))
            {
                foreach (var site in others.Take(MaxNamed))
                {
                    // A site deleted since its rows were written is gone with them, which the outbox finishes.
                    if (await sites.FindAsync(new SiteId(site)) is { } info)
                    {
                        named.Add(info);
                    }
                }
            }

            if (named.Count == 0)
            {
                return null;
            }

            var list = string.Join(", ", named.OrderBy(site => site.Name, StringComparer.Ordinal).ThenBy(site => site.Id).Select(site => $"{site.Id:D} ({site.Name})"));
            var more = others.Count > MaxNamed ? ", and more" : "";
            var option = $"{MediaOptions.Section}:{nameof(MediaOptions.Library)}";
            return new InsecureDefault
            {
                Id = HiddenSites,
                Message = $"{option} is Shared, so every site reads the media library of the default site only, and the assets these sites keep of their own are not found: {list}{more}. Their entries that refer to those assets show missing media. Nothing was moved or deleted; set {option}=PerSite to read each site's own library again.",
            };
        }
        catch (Exception exception)
        {
            MediaLog.SharedLibraryNotChecked(logger, exception.GetType().Name);
            return null;
        }
    }
}
