using FluentCMS.Authorization;
using FluentCMS.Content;
using FluentCMS.Media;
using FluentCMS.Plugins;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Plugins.Media;

// The assets of the library of the site of the current operation, as content shows them. On the Delivery API they are the
// public ones, whoever reads, which is what its media route serves to anyone; a private one is never shown there, and no
// grant is asked, since a Delivery principal holds none but reading content (4.8). So it is elsewhere, on /graphql say, for
// a principal that may not manage, which reads as on Delivery. Otherwise they are those the caller reaches with media.read,
// and a caller with no grant of it reaches none. In a site where the plugin is disabled, whose media routes answer 404, none
// is shown, whoever reads. A public asset has the URLs the Delivery API gives it; a private one has none, since only a
// download URL serves it.
internal sealed class MediaResolver(
    PluginDbContext<MediaPlugin> database,
    IPermissionChecker permissions,
    IOperationContextAccessor operations,
    MediaLibraries libraries,
    MediaService media,
    IPluginState plugins) : IMediaResolver
{
    public async Task<IReadOnlyDictionary<Guid, MediaDelivery>> ResolveAsync(IReadOnlyCollection<Guid> ids, string? locale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var operation = operations.Current ?? throw new InvalidOperationException("Media is resolved inside an operation, which names the site.");
        var site = operation.Site;
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, MediaDelivery>();
        }

        var library = await libraries.OfAsync(site, cancellationToken);
        if (await ShownAsync(operation, library, cancellationToken) is not { } shown)
        {
            return new Dictionary<Guid, MediaDelivery>();
        }

        var wanted = ids.Distinct().ToList();
        var assets = (await database.Set<Asset>().AsNoTracking()
                .Where(asset => wanted.Contains(asset.Id) && asset.SiteId == library.Value && asset.DeletedAt == null)
                .ToListAsync(cancellationToken))
            .Where(shown)
            .ToList();

        var presets = assets.Any(asset => !asset.IsPrivate) ? await media.PresetNamesAsync(library.Value, cancellationToken) : [];
        Dictionary<Guid, MediaDelivery> resolved = [];
        foreach (var asset in assets)
        {
            var urls = asset.IsPrivate ? null : await media.UrlsAsync(asset, cancellationToken, presets);
            resolved[asset.Id] = new MediaDelivery(MediaService.Info(asset, locale ?? operation.Locale), urls);
        }

        return resolved;
    }

    // Which assets of the library the operation shows, or null for none.
    private async Task<Func<Asset, bool>?> ShownAsync(OperationContext operation, SiteId library, CancellationToken cancellationToken)
    {
        if (!await plugins.IsEnabledAsync(MediaPlugin.Name, operation.Site, cancellationToken))
        {
            return null;
        }

        if (operation.IsDelivery || !await permissions.CanManageAsync(cancellationToken))
        {
            return asset => !asset.IsPrivate;
        }

        try
        {
            var reach = await FolderReach.OfAsync(database, permissions, MediaPermissions.Read, operation.Site, library, cancellationToken);
            return asset => reach.Includes(asset.FolderId);
        }
        catch (PermissionDeniedException)
        {
            return null;
        }
    }
}

// Lets core populate media fields, and the media nodes of rich text, through IMediaResolver, and check the assets a write
// names. What population returns is the asset's metadata in the locale of the read and the URLs of the Delivery API; who
// uploaded it, its folder and its tags are left out. A Delivery copy that shows an asset carries the tags of the media
// route's copy, so that the events that evict that copy, an asset made private or deleted among them, evict this one too.
internal sealed class MediaReferenceResolver(IMediaResolver media, PluginDbContext<MediaPlugin> database, IOperationContextAccessor operations, MediaLibraries libraries) : IReferenceResolver
{
    public string TargetType => MediaService.ReferenceTarget;

    public async Task<IReadOnlyDictionary<Guid, ResolvedReference>> ResolveAsync(IReadOnlyCollection<Guid> ids, string locale, CancellationToken cancellationToken = default)
    {
        var resolved = await media.ResolveAsync(ids, string.IsNullOrEmpty(locale) ? null : locale, cancellationToken);
        return resolved.ToDictionary(pair => pair.Key, pair => new ResolvedReference(Document(pair.Value))
        {
            Url = pair.Value.Urls?.Original,
            Text = pair.Value.Asset.Alt,
            CacheTags = [MediaCache.Asset(pair.Key), MediaCache.Presets(pair.Value.Asset.SiteId)],
        });
    }

    public async Task<IReadOnlySet<Guid>> FindMissingAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var site = (operations.Current ?? throw new InvalidOperationException("Media is checked inside an operation, which names the site.")).Site;
        var library = await libraries.OfAsync(site, cancellationToken);
        var wanted = ids.Distinct().ToList();
        var found = await database.Set<Asset>().AsNoTracking()
            .Where(asset => wanted.Contains(asset.Id) && asset.SiteId == library.Value && asset.DeletedAt == null)
            .Select(asset => asset.Id)
            .ToListAsync(cancellationToken);
        return wanted.Except(found).ToHashSet();
    }

    private static ContentDocument Document(MediaDelivery resolved)
    {
        var asset = resolved.Asset;
        ContentDocument? urls = null;
        if (resolved.Urls is { } given)
        {
            ContentDocument presets = [];
            foreach (var (name, url) in given.Presets)
            {
                presets[name] = url;
            }

            urls = new ContentDocument { ["original"] = given.Original, ["presets"] = presets, ["template"] = given.Template };
        }

        return new ContentDocument
        {
            ["id"] = asset.Id,
            ["type"] = MediaService.ReferenceTarget,
            ["fileName"] = asset.FileName,
            ["mimeType"] = asset.MimeType,
            ["size"] = asset.Size,
            ["width"] = asset.Width,
            ["height"] = asset.Height,
            ["title"] = asset.Title,
            ["alt"] = asset.Alt,
            ["caption"] = asset.Caption,
            ["credit"] = asset.Credit,
            ["focalPoint"] = asset.FocalPoint is { } point ? new ContentDocument { ["x"] = point.X, ["y"] = point.Y } : null,
            ["urls"] = urls,
        };
    }
}
