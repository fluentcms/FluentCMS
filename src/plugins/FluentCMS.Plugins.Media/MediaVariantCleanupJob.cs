using FluentCMS.Jobs;
using FluentCMS.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluentCMS.Plugins.Media;

internal sealed record MediaVariantCleanupPayload;

// Removes the variants of assets that are deleted, or gone, in batches: the file from the asset's storage, then the row. A
// file that cannot be deleted keeps its row for the next run. The rows a run leaves are the first in its order, since each
// batch is the first of what remains, so the next batch skips past them. Then it purges the assets deleted longer ago than
// FluentCms:Media:DeletedRetention whose variants are gone, in the same way: the original file, then the row.
internal sealed class MediaVariantCleanupJob(PluginDbContext<MediaPlugin> database, IFileStorage storage, IOptions<MediaOptions> options, TimeProvider time, ILogger<MediaVariantCleanupJob> logger)
    : IJob<MediaVariantCleanupPayload>
{
    public const string Type = "media.variants.cleanup";
    public const string Schedule = "45 4 * * *";
    public const int BatchSize = 500;

    public async Task ExecuteAsync(MediaVariantCleanupPayload payload, JobContext context)
    {
        var cancellationToken = context.CancellationToken;
        var removed = 0;
        var left = 0;
        while (true)
        {
            var batch = await database.Set<Variant>().AsNoTracking()
                .Where(variant => !database.Set<Asset>().Any(asset => asset.Id == variant.AssetId && asset.DeletedAt == null))
                .OrderBy(variant => variant.AssetId).ThenBy(variant => variant.ParamsHash)
                .Skip(left)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            var assetIds = batch.Select(variant => variant.AssetId).Distinct().ToList();
            var providers = await database.Set<Asset>().AsNoTracking()
                .Where(asset => assetIds.Contains(asset.Id))
                .ToDictionaryAsync(asset => asset.Id, asset => asset.StorageProvider, cancellationToken);

            var done = 0;
            foreach (var variant in batch)
            {
                var provider = providers.GetValueOrDefault(variant.AssetId, storage.Name);
                if (provider != storage.Name)
                {
                    MediaLog.VariantNotDeleted(logger, provider, variant.StorageKey, null);
                    continue;
                }

                try
                {
                    await storage.DeleteAsync(variant.StorageKey, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    MediaLog.VariantNotDeleted(logger, provider, variant.StorageKey, exception);
                    continue;
                }

                done += await database.Set<Variant>()
                    .Where(item => item.AssetId == variant.AssetId && item.ParamsHash == variant.ParamsHash)
                    .ExecuteDeleteAsync(cancellationToken);
            }

            removed += done;
            left += batch.Count - done;
        }

        MediaLog.VariantsRemoved(logger, removed);
        var purged = await PurgeAsync(time.GetUtcNow() - options.Value.DeletedRetention, cancellationToken);
        MediaLog.AssetsPurged(logger, purged);
    }

    private async Task<int> PurgeAsync(DateTimeOffset deletedBefore, CancellationToken cancellationToken)
    {
        var purged = 0;
        var left = 0;
        while (true)
        {
            var batch = await database.Set<Asset>().AsNoTracking()
                .Where(asset => asset.DeletedAt != null && asset.DeletedAt < deletedBefore && !database.Set<Variant>().Any(variant => variant.AssetId == asset.Id))
                .OrderBy(asset => asset.Id)
                .Skip(left)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                return purged;
            }

            var done = 0;
            foreach (var asset in batch)
            {
                if (asset.StorageProvider != storage.Name)
                {
                    MediaLog.AssetNotPurged(logger, asset.Id, asset.StorageProvider, asset.StorageKey, null);
                    continue;
                }

                try
                {
                    await storage.DeleteAsync(asset.StorageKey, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    MediaLog.AssetNotPurged(logger, asset.Id, asset.StorageProvider, asset.StorageKey, exception);
                    continue;
                }

                done += await database.Set<Asset>().Where(item => item.Id == asset.Id && item.DeletedAt != null).ExecuteDeleteAsync(cancellationToken);
            }

            purged += done;
            left += batch.Count - done;
        }
    }
}
