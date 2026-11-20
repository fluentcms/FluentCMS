using FluentCMS.Jobs;
using FluentCMS.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins.Media;

// After is the id of the last image the run before read, or null for the first run. BatchSize caps the images a run reads,
// so that a run is short; the rest waits for the next.
internal sealed record MediaDimensionsPayload(Guid? After, int BatchSize = 100);

// Gives the images stored before their size was read through the image processor the size they are shown at, after their
// EXIF orientation. The media startup enqueues a first run at each start of the host. A run reads the files of at most
// BatchSize images that are not deleted, in the order of their ids, writes the width and height that differ, and enqueues
// the next run after the last image it read. Only the width and height change: not the version or the time and author of
// the last update, and no event of the asset is raised, so no webhook hears of it; the copies of the Delivery API that
// name a corrected asset are evicted. Another run over the same images finds nothing to write, so a run tried again, or a
// second chain started by another start or another instance, does no harm. An image whose file cannot be read keeps its
// size and is logged, and one replaced since the run read it keeps the size of its new file.
internal sealed class MediaDimensionsJob(
    PluginDbContext<MediaPlugin> database,
    MediaStorage storages,
    IImageProcessor processor,
    MediaCache cache,
    IJobScheduler scheduler,
    ILogger<MediaDimensionsJob> logger) : IJob<MediaDimensionsPayload>
{
    public const string Type = "media.dimensions.backfill";

    public async Task ExecuteAsync(MediaDimensionsPayload payload, JobContext context)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfLessThan(payload.BatchSize, 1);

        var cancellationToken = context.CancellationToken;
        var types = MediaTypes.Allowable.Where(type => type.IsImage && processor.CanDecode(type.MimeType)).ToDictionary(type => type.MimeType, StringComparer.Ordinal);
        var mimeTypes = types.Keys.ToList();
        var images = database.Set<Asset>().AsNoTracking().Where(asset => asset.DeletedAt == null && mimeTypes.Contains(asset.MimeType));
        if (payload.After is { } after)
        {
            images = images.Where(asset => asset.Id.CompareTo(after) > 0);
        }

        var batch = await images.OrderBy(asset => asset.Id)
            .Select(asset => new { asset.Id, asset.StorageProvider, asset.StorageKey, asset.MimeType, asset.Checksum, asset.Width, asset.Height })
            .Take(payload.BatchSize)
            .ToListAsync(cancellationToken);

        List<string> corrected = [];
        foreach (var image in batch)
        {
            if (await ReadAsync(image.Id, image.StorageProvider, image.StorageKey, types[image.MimeType], cancellationToken) is not { } shown
                || (shown.Width == image.Width && shown.Height == image.Height))
            {
                continue;
            }

            var written = await database.Set<Asset>()
                .Where(asset => asset.Id == image.Id && asset.Checksum == image.Checksum && asset.DeletedAt == null)
                .ExecuteUpdateAsync(set => set.SetProperty(asset => asset.Width, shown.Width).SetProperty(asset => asset.Height, shown.Height), cancellationToken);
            if (written > 0)
            {
                corrected.Add(MediaCache.Asset(image.Id));
            }
        }

        if (corrected.Count > 0)
        {
            await cache.EvictAsync([.. corrected], cancellationToken);
        }

        MediaLog.DimensionsChecked(logger, batch.Count, corrected.Count);
        if (batch.Count == payload.BatchSize)
        {
            await scheduler.EnqueueAsync<MediaDimensionsJob, MediaDimensionsPayload>(payload with { After = batch[^1].Id }, cancellationToken: cancellationToken);
        }
    }

    private async Task<(int Width, int Height)?> ReadAsync(Guid id, string provider, string key, MediaType type, CancellationToken cancellationToken)
    {
        if (storages.Find(provider) is not { } storage)
        {
            MediaLog.DimensionsNotRead(logger, id, provider, key, null);
            return null;
        }

        try
        {
            if (await storage.OpenReadAsync(key, cancellationToken) is not { } content)
            {
                MediaLog.DimensionsNotRead(logger, id, provider, key, null);
                return null;
            }

            await using var source = await MediaTransforms.SpoolAsync(content, MediaService.TemporaryFile, cancellationToken);
            return await MediaService.DimensionsAsync(processor, source, type, cancellationToken);
        }
        // A storage's own timeout is a failure of that file; only the job's token stops the run.
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            MediaLog.DimensionsNotRead(logger, id, provider, key, exception);
            return null;
        }
    }
}
