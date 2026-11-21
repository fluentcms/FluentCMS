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
// FluentCms:Media:DeletedRetention whose variants are gone, in the same way: the original file, then the row. Each file is
// deleted from the storage its asset recorded. Files go as after a replace (ReplacedFiles): only the rows of an asset whose
// key a line named, and a file that another asset names keeps its row too, so that the asset that owns it deletes it once
// no other names it. The assets whose key a line named are purged first, so that the owner of their key can go in the run.
internal sealed class MediaVariantCleanupJob(PluginDbContext<MediaPlugin> database, MediaStorage storages, IOptions<MediaOptions> options, TimeProvider time, ILogger<MediaVariantCleanupJob> logger)
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
            var assets = await database.Set<Asset>().AsNoTracking()
                .Where(asset => assetIds.Contains(asset.Id))
                .ToDictionaryAsync(asset => asset.Id, cancellationToken);

            var done = 0;
            foreach (var variants in batch.GroupBy(variant => variant.AssetId))
            {
                var asset = assets.GetValueOrDefault(variants.Key);
                var keys = variants.Select(variant => variant.StorageKey).ToList();
                var named = asset is { OwnsFile: false } ? []
                    : (await (asset is null ? ReplacedFiles.NamedByAny(database, variants.Key, keys) : ReplacedFiles.NamedByOthers(database, asset, keys)).ToListAsync(cancellationToken))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var variant in variants)
                {
                    if (named.Contains(variant.StorageKey))
                    {
                        // The owner keeps the row, to delete the file once no other names it; a gone asset owns nothing.
                        if (asset is not null)
                        {
                            continue;
                        }
                    }
                    else if (asset is not { OwnsFile: false })
                    {
                        var provider = asset?.StorageProvider ?? storages.Current.Name;
                        if (storages.Find(provider) is not { } storage)
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
                    }

                    done += await database.Set<Variant>()
                        .Where(item => item.AssetId == variant.AssetId && item.ParamsHash == variant.ParamsHash)
                        .ExecuteDeleteAsync(cancellationToken);
                }
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
                .OrderBy(asset => asset.OwnsFile).ThenBy(asset => asset.Id)
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
                if (!asset.OwnsFile)
                {
                    done += await database.Set<Asset>().Where(item => item.Id == asset.Id && item.DeletedAt != null).ExecuteDeleteAsync(cancellationToken);
                    continue;
                }

                if (await ReplacedFiles.NamedByOthers(database, asset, [asset.StorageKey]).AnyAsync(cancellationToken))
                {
                    continue;
                }

                if (storages.Find(asset.StorageProvider) is not { } storage)
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
