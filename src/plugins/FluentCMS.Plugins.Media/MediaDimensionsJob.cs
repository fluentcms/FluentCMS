using FluentCMS.Jobs;
using FluentCMS.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins.Media;

// BatchSize caps the images a run reads, so that a run is short; the rest waits for the next.
internal sealed record MediaDimensionsPayload(int BatchSize = 100);

// Gives the images stored before their size was read through the image processor the size they are shown at, after their
// orientation. Only types that carry one are read: JPEG, WebP and AVIF, of those the processor reads. One chain of runs
// goes over the library once per database. Where it stands is a row of its own in cms_media_signing_key (StateId, beside
// the signing key's row 1, which is the only row the signing reads): its key is the id of the last image read, empty
// before the first, or "done", and its created_at the time a start claimed it or a run last moved it on, so that a chain
// that goes on keeps its claim. A start of the host claims the check
// only when the row is missing, or names no end and was claimed longer ago than Lease, as after a chain whose run failed
// for good, and then enqueues the first run (MediaDimensionsStart), so that instances that start together start one chain.
// A run reads the next BatchSize images that are not deleted, in the order of their ids, writes the width and height that
// differ, and moves the row on from where it read it, then enqueues the next run, or writes "done"; two chains, which a
// stale claim can leave, take turns on the row. Deleting the row runs the check again at the next start. Only the width
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
    MediaStorage storages,
    IImageProcessor processor,
    MediaCache cache,
    IJobScheduler scheduler,
    TimeProvider time,
    ILogger<MediaDimensionsJob> logger) : IJob<MediaDimensionsPayload>
{
    public const string Type = "media.dimensions.backfill";
    public const int StateId = 2;
    public const string Done = "done";
    public const int PrefixLength = 1 << 20;

    public static readonly TimeSpan Lease = TimeSpan.FromHours(1);

    private static readonly MediaType[] Oriented = [MediaTypes.Jpeg, MediaTypes.WebP, MediaTypes.Avif];

    // At a start of the host: claims the check unless the library was gone over or a chain claimed it within Lease, by adding
    // the row or by moving the time of a stale claim. Answers the claim, which MediaDimensionsStart enqueues a run for, or
    // null. The time is cut to milliseconds, which every provider stores as it is, so that the claim reads back equal.
    public static async Task<DateTimeOffset?> ClaimAsync(PluginDbContext<MediaPlugin> database, TimeProvider time, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        now = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMillisecond));
        var state = await database.Set<SigningKey>().AsNoTracking().FirstOrDefaultAsync(row => row.Id == StateId, cancellationToken);
        if (state is null)
        {
            var claim = new SigningKey { Id = StateId, Key = "", CreatedAt = now };
            database.Add(claim);
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                return now;
            }
            catch (DbUpdateException)
            {
                database.Entry(claim).State = EntityState.Detached;

                // Another instance claimed it at the same start; a failure of the database is no claim.
                if (!await database.Set<SigningKey>().AsNoTracking().AnyAsync(row => row.Id == StateId, cancellationToken))
                {
                    throw;
                }

                return null;
            }
        }

        if (state.Key == Done || state.CreatedAt > now - Lease)
        {
            return null;
        }

        var moved = await database.Set<SigningKey>().Where(row => row.Id == StateId && row.Key == state.Key && row.CreatedAt == state.CreatedAt)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.CreatedAt, now), cancellationToken);
        return moved > 0 ? now : null;
    }

    public async Task ExecuteAsync(MediaDimensionsPayload payload, JobContext context)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfLessThan(payload.BatchSize, 1);

        var cancellationToken = context.CancellationToken;
        if (await database.Set<SigningKey>().AsNoTracking().FirstOrDefaultAsync(row => row.Id == StateId, cancellationToken) is not { } state || state.Key == Done)
        {
            return;
        }

        var types = Oriented.Where(type => processor.CanDecode(type.MimeType)).ToDictionary(type => type.MimeType, StringComparer.OrdinalIgnoreCase);
        var mimeTypes = types.Keys.ToList();
#pragma warning disable CA1304, CA1311 // The database lowers the column, where lower() takes no culture.
        var images = database.Set<Asset>().AsNoTracking().Where(asset => asset.DeletedAt == null && mimeTypes.Contains(asset.MimeType.ToLower()));
#pragma warning restore CA1304, CA1311
        if (Guid.TryParse(state.Key, out var after))
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
        var moved = await database.Set<SigningKey>().Where(row => row.Id == StateId && row.Key == state.Key)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.Key, next).SetProperty(row => row.CreatedAt, time.GetUtcNow()), cancellationToken);
        if (moved > 0 && next != Done)
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
