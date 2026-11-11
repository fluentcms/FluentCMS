using FluentCMS.Authorization;
using FluentCMS.Content;
using FluentCMS.Media;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Plugins.Media;

// The assets of the site of the current operation that the caller reaches with media.read, as content shows them. A caller
// with no grant of it reaches none. A public asset has the URLs the Delivery API gives it; a private one has none, since
// only a download URL serves it.
internal sealed class MediaResolver(
    PluginDbContext<MediaPlugin> database,
    IPermissionChecker permissions,
    IOperationContextAccessor operations,
    MediaLibraries libraries,
    MediaService media) : IMediaResolver
{
    public async Task<IReadOnlyDictionary<Guid, MediaDelivery>> ResolveAsync(IReadOnlyCollection<Guid> ids, string? locale = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var operation = operations.Current ?? throw new InvalidOperationException("Media is resolved inside an operation, which names the site.");
        var site = operation.Site;
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, MediaDelivery>();
        }

        var library = await libraries.OfAsync(site, cancellationToken);
        FolderReach reach;
        try
        {
            reach = await FolderReach.OfAsync(database, permissions, MediaPermissions.Read, site, library, cancellationToken);
        }
        catch (PermissionDeniedException)
        {
            return new Dictionary<Guid, MediaDelivery>();
        }

        var wanted = ids.Distinct().ToList();
        var assets = (await database.Set<Asset>().AsNoTracking()
                .Where(asset => wanted.Contains(asset.Id) && asset.SiteId == library.Value && asset.DeletedAt == null)
                .ToListAsync(cancellationToken))
            .Where(asset => reach.Includes(asset.FolderId))
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
}

// Lets core populate media fields, and the media nodes of rich text, through IMediaResolver, and check the assets a write
// names. What population returns is the asset's metadata in the locale of the read and the URLs of the Delivery API; who
// uploaded it, its folder and its tags are left out.
internal sealed class MediaReferenceResolver(IMediaResolver media, PluginDbContext<MediaPlugin> database, IOperationContextAccessor operations, MediaLibraries libraries) : IReferenceResolver
{
    public string TargetType => MediaService.ReferenceTarget;

    public async Task<IReadOnlyDictionary<Guid, ResolvedReference>> ResolveAsync(IReadOnlyCollection<Guid> ids, string locale, CancellationToken cancellationToken = default)
    {
        var resolved = await media.ResolveAsync(ids, string.IsNullOrEmpty(locale) ? null : locale, cancellationToken);
        return resolved.ToDictionary(pair => pair.Key, pair => new ResolvedReference(Document(pair.Value)) { Url = pair.Value.Urls?.Original, Text = pair.Value.Asset.Alt });
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
