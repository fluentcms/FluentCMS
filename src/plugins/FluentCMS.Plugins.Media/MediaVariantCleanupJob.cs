using FluentCMS.Jobs;
using FluentCMS.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins.Media;

internal sealed record MediaVariantCleanupPayload;

// Removes the variants of assets that are deleted, or gone, in batches: the file from the asset's storage, then the row. A
// file that cannot be deleted keeps its row for the next run. The rows a run leaves are the first in its order, since each
// batch is the first of what remains, so the next batch skips past them.
internal sealed class MediaVariantCleanupJob(PluginDbContext<MediaPlugin> database, IFileStorage storage, ILogger<MediaVariantCleanupJob> logger)
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
    }
}
