using FluentCMS.Jobs;
using FluentCMS.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins.Media;

// BatchSize caps the images a run reads, so that a run is short; the rest waits for the next.
internal sealed record MediaDimensionsPayload(int BatchSize = 100);

// Gives the images stored before their size was read through the image processor the size they are shown at, after their
// orientation. Only types that carry one are read: JPEG, WebP and AVIF, of those the processor reads. One chain of runs
// goes over the library once per database. Where it stands is the plugin's value StateKey: the id of the last image read,
// empty before the first, or "done", written when a start claimed it or a run last moved it on, so that a chain that goes
// on keeps its claim. A start of the host claims the check only when the value is missing, or names no end and was written
// longer ago than Lease, as after a chain whose run failed for good, and then enqueues the first run (MediaDimensionsStart),
// so that instances that start together start one chain. A run reads the next BatchSize images that are not deleted, in
// the order of their ids, writes the width and height that differ, and moves the value on from the version it read, then
// enqueues the next run, or writes "done". A claim taken over a stale one moves the version on, so a chain that still held
// the stale claim loses its next write and ends there, and the new chain reads that batch again. Deleting the value runs
// the check again at the next start. Only the width
// and height change: not the version or the time and author of the last update, and no event of the asset is raised, so
// no webhook hears of it; the copies of the Delivery API that name a corrected asset are evicted. A JPEG is identified from
// the first PrefixLength bytes of its file, where its EXIF and frame header are, and from the whole file only when those
// are not enough; WebP and AVIF can put their orientation after the image, so the whole file is read. An image whose file
// is missing, or whose bytes the processor does not take, keeps its size and is logged; a failure of the storage, or of the
// disk under the processor, fails the run, which the runner tries again from the same place, and on the last attempt the
// image is logged and skipped, so that one file that can never be read, such as an archived object, does not stop the
// check. A retry evicts the Delivery copies of the whole batch, which an attempt before may have corrected without
// evicting. One replaced since the run read it keeps the size of its new file.
internal sealed class MediaDimensionsJob(
    PluginDbContext<MediaPlugin> database,
    IPluginStore<MediaPlugin> store,
    MediaStorage storages,
    IImageProcessor processor,
    MediaCache cache,
    IJobScheduler scheduler,
    ILogger<MediaDimensionsJob> logger) : IJob<MediaDimensionsPayload>
{
    public const string Type = "media.dimensions.backfill";
    public const string StateKey = Type;
    public const string Done = "done";
    public const int PrefixLength = 1 << 20;

    public static readonly TimeSpan Lease = TimeSpan.FromHours(1);

    // Far past a run of the default 100 images, each read from its storage once or twice. It is not resumable: a run moves
    // the cursor only at its end, so one stopped there would end the chain.
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromMinutes(30);

    private static readonly MediaType[] Oriented = [MediaTypes.Jpeg, MediaTypes.WebP, MediaTypes.Avif];

    // At a start of the host: claims the check unless the library was gone over or a chain claimed it within Lease, by adding
    // the value or by writing a stale claim again at its version. Answers the version of the claim, which MediaDimensionsStart
    // enqueues a run for, or null when another start holds it.
    public static async Task<long?> ClaimAsync(IPluginStore<MediaPlugin> store, TimeProvider time, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(time);

        if (await store.GetAsync(StateKey, cancellationToken) is not { } state)
        {
            return (await store.TryAddAsync(StateKey, "", cancellationToken))?.Version;
        }

        if (state.Value == Done || state.UpdatedAt > time.GetUtcNow() - Lease)
        {
            return null;
        }

        return (await store.TrySetAsync(StateKey, state.Value, state.Version, cancellationToken))?.Version;
    }

    public async Task ExecuteAsync(MediaDimensionsPayload payload, JobContext context)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfLessThan(payload.BatchSize, 1);

        var cancellationToken = context.CancellationToken;
        if (await store.GetAsync(StateKey, cancellationToken) is not { } state || state.Value == Done)
        {
            return;
        }

        var types = Oriented.Where(type => processor.CanDecode(type.MimeType)).ToDictionary(type => type.MimeType, StringComparer.OrdinalIgnoreCase);
        var mimeTypes = types.Keys.ToList();
#pragma warning disable CA1304, CA1311 // The database lowers the column, where lower() takes no culture.
        var images = database.Set<Asset>().AsNoTracking().Where(asset => asset.DeletedAt == null && mimeTypes.Contains(asset.MimeType.ToLower()));
#pragma warning restore CA1304, CA1311
        if (Guid.TryParse(state.Value, out var after))
        {
            images = images.Where(asset => asset.Id.CompareTo(after) > 0);
        }

        var batch = await images.OrderBy(asset => asset.Id)
            .Select(asset => new { asset.Id, asset.StorageProvider, asset.StorageKey, asset.MimeType, asset.Checksum, asset.Width, asset.Height })
            .Take(payload.BatchSize)
            .ToListAsync(cancellationToken);

        var retry = context.Attempt > 1;
        var last = context.Attempt >= context.MaxAttempts;
        List<string> corrected = [];
        foreach (var image in batch)
        {
            (int Width, int Height)? read;
            try
            {
                read = await ReadAsync(image.Id, image.StorageProvider, image.StorageKey, types.GetValueOrDefault(image.MimeType), cancellationToken);
            }
            // A storage's own timeout is a failure of that file; only the job's token stops the run.
            catch (Exception exception) when (last && !cancellationToken.IsCancellationRequested)
            {
                MediaLog.DimensionsNotRead(logger, image.Id, image.StorageProvider, image.StorageKey, exception);
                continue;
            }

            if (read is not { } shown || (shown.Width == image.Width && shown.Height == image.Height))
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

        string[] evicted = retry ? [.. batch.Select(image => MediaCache.Asset(image.Id))] : [.. corrected];
        if (evicted.Length > 0)
        {
            await cache.EvictAsync(evicted, cancellationToken);
        }

        MediaLog.DimensionsChecked(logger, batch.Count, corrected.Count);
        var next = batch.Count < payload.BatchSize ? Done : batch[^1].Id.ToString("D");
        var moved = await store.TrySetAsync(StateKey, next, state.Version, cancellationToken);
        if (moved is not null && next != Done)
        {
            await scheduler.EnqueueAsync<MediaDimensionsJob, MediaDimensionsPayload>(payload, cancellationToken: cancellationToken);
        }
    }

    // The size the processor reads, or null, which keeps the asset's: a type of no list, a storage the instance no longer
    // has, a file that is missing, or bytes the processor does not take, each logged. A failure of the storage fails the run,
    // so that the runner tries the batch again and the cursor stays where it was.
    private async Task<(int Width, int Height)?> ReadAsync(Guid id, string provider, string key, MediaType? type, CancellationToken cancellationToken)
    {
        if (type is null)
        {
            return null;
        }

        if (storages.Find(provider) is not { } storage)
        {
            MediaLog.DimensionsNotRead(logger, id, provider, key, null);
            return null;
        }

        if (type == MediaTypes.Jpeg)
        {
            var prefix = new byte[PrefixLength];
            int read;
            await using (var content = await storage.OpenReadAsync(key, cancellationToken))
            {
                if (content is null)
                {
                    MediaLog.DimensionsNotRead(logger, id, provider, key, null);
                    return null;
                }

                read = await content.ReadAtLeastAsync(prefix, prefix.Length, throwOnEndOfStream: false, cancellationToken);
            }

            using var head = new MemoryStream(prefix, 0, read, writable: false);
            var (identified, failure) = await IdentifyAsync(head, cancellationToken);
            if (identified is not null)
            {
                return identified;
            }

            if (read < prefix.Length)
            {
                MediaLog.DimensionsNotRead(logger, id, provider, key, failure);
                return null;
            }
        }

        await using var whole = await storage.OpenReadAsync(key, cancellationToken);
        if (whole is null)
        {
            MediaLog.DimensionsNotRead(logger, id, provider, key, null);
            return null;
        }

        await using var source = await MediaTransforms.SpoolAsync(whole, MediaService.TemporaryFile, cancellationToken);
        var (size, error) = await IdentifyAsync(source, cancellationToken);
        if (size is null)
        {
            MediaLog.DimensionsNotRead(logger, id, provider, key, error);
        }

        return size;
    }

    // Bytes the processor does not take answer null, with what it threw, if anything. A failure to read the file, which on
    // the local disk the processor reads itself, is one of the storage and goes through.
    private async Task<((int Width, int Height)? Size, Exception? Failure)> IdentifyAsync(Stream image, CancellationToken cancellationToken)
    {
        try
        {
            return (await processor.IdentifyAsync(image, cancellationToken) is { Width: > 0, Height: > 0 } size ? (size.Width, size.Height) : null, null);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or IOException or UnauthorizedAccessException))
        {
            return (null, exception);
        }
    }
}
