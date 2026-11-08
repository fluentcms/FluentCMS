using FluentCMS.Authorization;
using FluentCMS.Content;
using FluentCMS.Media;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Plugins.Media;

// The assets of the site of the current operation that the caller reaches with media.read, as content shows them. A caller
// with no grant of it reaches none. A public asset has the signed URL of its file and, when the processor reads its type,
// one per preset of the site; a private one has none, since only a signed download serves it.
internal sealed class MediaResolver(
    PluginDbContext<MediaPlugin> database,
    IPermissionChecker permissions,
    IOperationContextAccessor operations,
    IMediaService media,
    IImageProcessor processor) : IMediaResolver
{
    public async Task<IReadOnlyDictionary<Guid, ResolvedMedia>> ResolveAsync(IReadOnlyCollection<Guid> ids, string? locale = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var operation = operations.Current ?? throw new InvalidOperationException("Media is resolved inside an operation, which names the site.");
        var site = operation.Site;
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, ResolvedMedia>();
        }

        FolderReach reach;
        try
        {
            reach = await FolderReach.OfAsync(database, permissions, MediaPermissions.Read, site, cancellationToken);
        }
        catch (PermissionDeniedException)
        {
            return new Dictionary<Guid, ResolvedMedia>();
        }

        var wanted = ids.Distinct().ToList();
        var assets = (await database.Set<Asset>().AsNoTracking()
                .Where(asset => wanted.Contains(asset.Id) && asset.SiteId == site.Value && asset.DeletedAt == null)
                .ToListAsync(cancellationToken))
            .Where(asset => reach.Includes(asset.FolderId))
            .ToList();

        List<string>? presets = null;
        if (assets.Any(asset => !asset.IsPrivate && processor.CanDecode(asset.MimeType)))
        {
            presets = await database.Set<Preset>().AsNoTracking().Where(preset => preset.SiteId == site.Value).Select(preset => preset.Name).ToListAsync(cancellationToken);
            presets.Sort(StringComparer.Ordinal);
        }

        Dictionary<Guid, ResolvedMedia> resolved = [];
        foreach (var asset in assets)
        {
            var info = MediaService.Info(asset, locale ?? operation.Locale);
            if (asset.IsPrivate)
            {
                resolved[asset.Id] = new ResolvedMedia(info);
                continue;
            }

            Dictionary<string, string> urls = new(StringComparer.Ordinal);
            if (presets is not null && processor.CanDecode(asset.MimeType))
            {
                foreach (var name in presets)
                {
                    urls[name] = await media.GetImageUrlAsync(info, new MediaImageOptions { Preset = name }, cancellationToken);
                }
            }

            resolved[asset.Id] = new ResolvedMedia(info) { Url = await media.GetImageUrlAsync(info, null, cancellationToken), PresetUrls = urls };
        }

        return resolved;
    }
}

// Lets core populate media fields, and the media nodes of rich text, through IMediaResolver, and check the assets a write
// names. What population returns is the asset's metadata in the locale of the read and its URLs; who uploaded it is left out.
internal sealed class MediaReferenceResolver(IMediaResolver media, PluginDbContext<MediaPlugin> database, IOperationContextAccessor operations) : IReferenceResolver
{
    public string TargetType => MediaFieldType.TypeName;

    public async Task<IReadOnlyDictionary<Guid, ResolvedReference>> ResolveAsync(IReadOnlyCollection<Guid> ids, string locale, CancellationToken cancellationToken = default)
    {
        var resolved = await media.ResolveAsync(ids, string.IsNullOrEmpty(locale) ? null : locale, cancellationToken);
        return resolved.ToDictionary(pair => pair.Key, pair => new ResolvedReference(Document(pair.Value)) { Url = pair.Value.Url, Text = pair.Value.Asset.Alt });
    }

    public async Task<IReadOnlySet<Guid>> FindMissingAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var site = (operations.Current ?? throw new InvalidOperationException("Media is checked inside an operation, which names the site.")).Site;
        var wanted = ids.Distinct().ToList();
        var found = await database.Set<Asset>().AsNoTracking()
            .Where(asset => wanted.Contains(asset.Id) && asset.SiteId == site.Value && asset.DeletedAt == null)
            .Select(asset => asset.Id)
            .ToListAsync(cancellationToken);
        return wanted.Except(found).ToHashSet();
    }

    private static ContentDocument Document(ResolvedMedia resolved)
    {
        var asset = resolved.Asset;
        ContentDocument presets = [];
        foreach (var (name, url) in resolved.PresetUrls)
        {
            presets[name] = url;
        }

        return new ContentDocument
        {
            ["id"] = asset.Id,
            ["type"] = MediaFieldType.TypeName,
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
            ["url"] = resolved.Url,
            ["presets"] = presets,
        };
    }
}
