using FluentCMS.Content;
using FluentCMS.Settings;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Plugins.Media;

// `fluentcms media sanitize-svg` (#3605): the SVGs of every library, not deleted, read again with the current pass. One
// whose bytes the pass changes is listed, and without the dry run replaced as a replace does it (MediaService.ResanitizeAsync),
// in an operation of its site, so that its event reaches that site's webhooks. One whose bytes the pass keeps is left alone,
// so a second run changes nothing. One larger than the site's MaxSvgFileSize is not parsed, one the pass no longer takes as
// an SVG is not replaced, and neither is deleted; each is named and counted, with one whose file is missing or whose replace
// was refused. With a shared library only the default site's rows are read, the only ones the service finds. Answers how
// many were left.
internal sealed class StoredSvgSanitizer(
    PluginDbContext<MediaPlugin> database,
    MediaService media,
    MediaLibraries libraries,
    MediaStorage storages,
    ISettings<MediaSettings> settings,
    IOperationContextAccessor operations,
    TimeProvider time)
{
    // Core runs the command and names the same key, since neither references the other.
    public const string Key = "FluentCMS.Media.SanitizeSvg";

    public async Task<int> RunAsync(bool dryRun, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var svg = MediaTypes.Svg.MimeType;
#pragma warning disable CA1304, CA1311, CA1862 // The database lowers the column, where lower() takes no culture.
        var stored = database.Set<Asset>().AsNoTracking().Where(asset => asset.DeletedAt == null && asset.MimeType.ToLower() == svg);
#pragma warning restore CA1304, CA1311, CA1862
        var sites = await stored.Select(asset => asset.SiteId).Distinct().ToListAsync(cancellationToken);

        int changed = 0, kept = 0, over = 0, refused = 0, failed = 0;
        foreach (var site in sites.Order())
        {
            if ((await libraries.OfAsync(new SiteId(site), cancellationToken)).Value != site)
            {
                continue;
            }

            using var operation = SystemOperation.Begin(operations, time, Origin.Cli, new SiteId(site));
            var limit = (await settings.GetAsync(new SiteId(site), cancellationToken)).MaxSvgFileSize;
            var assets = await stored.Where(asset => asset.SiteId == site).OrderBy(asset => asset.Id)
                .Select(asset => new { asset.Id, asset.FileName, asset.StorageProvider, asset.StorageKey, asset.Size, asset.Version })
                .ToListAsync(cancellationToken);
            foreach (var asset in assets)
            {
                var name = $"{SharedLibraryReport.Printable(asset.FileName)} (asset {asset.Id:D} of site {site:D})";
                var bytes = asset.Size > limit ? null : await ReadAsync(asset.StorageProvider, asset.StorageKey, limit, cancellationToken);
                if (asset.Size > limit || bytes?.Length > limit)
                {
                    over++;
                    await error.WriteLineAsync($"Left {name}, of {asset.Size} bytes, as it is: it is over the site's maxSvgFileSize of {limit} bytes, so it was not parsed; raise the setting for the run, or replace or delete it by hand.");
                    continue;
                }

                if (bytes is null)
                {
                    failed++;
                    await error.WriteLineAsync($"Left {name} as it is: its file is not in the storage '{asset.StorageProvider}'.");
                    continue;
                }

                if (SvgDocument.Sanitized(new MemoryStream(bytes, writable: false), limit, cancellationToken) is not { } sanitized)
                {
                    refused++;
                    await error.WriteLineAsync($"Left {name} as it is: the pass no longer takes it as an SVG; replace or delete it by hand.");
                    continue;
                }

                if (sanitized.AsSpan().SequenceEqual(bytes))
                {
                    kept++;
                    continue;
                }

                if (dryRun)
                {
                    changed++;
                    await output.WriteLineAsync($"Would sanitise {name}: {bytes.Length} bytes, {sanitized.Length} after.");
                    continue;
                }

                try
                {
                    var replaced = await media.ResanitizeAsync(asset.Id, asset.Version, asset.FileName, sanitized, cancellationToken);
                    changed++;
                    await output.WriteLineAsync($"Sanitised {name}: {bytes.Length} bytes, {replaced.Size} after, checksum {replaced.Checksum}, version {replaced.Version}.");
                }
                catch (PluginException refusal)
                {
                    failed++;
                    await error.WriteLineAsync($"Left {name} as it is: {refusal.Code}: {refusal.Message}");
                }
                finally
                {
                    database.ChangeTracker.Clear();
                }
            }
        }

        var left = over + refused + failed;
        await output.WriteLineAsync(dryRun
            ? $"{changed} stored SVGs would be sanitised again and {kept} are as the pass writes them; {left} would be left as they are: {over} over maxSvgFileSize, {refused} that the pass refuses and {failed} not read. Nothing was changed."
            : $"Sanitised {changed} stored SVGs again and {kept} were as the pass writes them; {left} are left as they are: {over} over maxSvgFileSize, {refused} that the pass refuses and {failed} not read or not replaced.");
        if (changed > 0)
        {
            var cacheControl = $"{MediaOptions.Section}:{nameof(MediaOptions.CacheControl)}";
            await output.WriteLineAsync(dryRun
                ? $"Sanitising them changes their checksums and the v of their URLs. A URL with an old v still serves the new bytes, since v is signed and not compared, but a CDN serves the old bytes under it until its copy expires ({cacheControl})."
                : $"Their checksums changed, and so did the v of their URLs. A URL with an old v still serves the new bytes, since v is signed and not compared, but a CDN serves the old bytes under it until its copy expires ({cacheControl}).");
        }

        return left;
    }

    // The file, read until it is past the limit, at most one buffer beyond it; null when it is missing or its storage is gone.
    private async Task<byte[]?> ReadAsync(string provider, string key, long limit, CancellationToken cancellationToken)
    {
        if (storages.Find(provider) is not { } storage)
        {
            return null;
        }

        await using var content = await storage.OpenReadAsync(key, cancellationToken);
        if (content is null)
        {
            return null;
        }

        using var copy = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while (copy.Length <= limit && (read = await content.ReadAsync(buffer, cancellationToken)) > 0)
        {
            copy.Write(buffer, 0, read);
        }

        return copy.ToArray();
    }
}
