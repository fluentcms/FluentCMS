using FluentCMS.Sites;
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
