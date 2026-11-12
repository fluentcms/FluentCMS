using FluentCMS.Events;
using FluentCMS.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins.Media;

// The library of a deleted site goes with it, through the outbox: its presets and folders, then each asset with its
// variants, the files from the storage the asset recorded and then the rows. Every asset is marked deleted first, so that
// none is served while the files go and what this cannot remove is still the daily cleanup's: a file that cannot be deleted
// keeps its asset as a deleted row, which the cleanup purges with the file after FluentCms:Media:DeletedRetention, and the
// site's deletion stands. A run cut short, or a second delivery, goes on with what is left; a library too large for the
// outbox's attempts is left to the cleanup in the same way, since every asset was marked first. A shared library is the default
// site's, which cannot be deleted, so only rows a site kept while the library was per site are found.
internal sealed class DeletedSiteMedia(PluginDbContext<MediaPlugin> database, MediaStorage storages, TimeProvider time, ILogger<DeletedSiteMedia> logger)
    : IOutboxEventHandler<SiteDeletedEvent>
{
    internal int BatchSize { get; init; } = 500;

    public async ValueTask HandleAsync(SiteDeletedEvent message, EventDelivery delivery, CancellationToken cancellationToken)
    {
        var site = message.SiteId;
        var now = time.GetUtcNow();
        await database.Set<Asset>().Where(asset => asset.SiteId == site && asset.DeletedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(asset => asset.DeletedAt, now), cancellationToken);
        await database.Set<Preset>().Where(preset => preset.SiteId == site).ExecuteDeleteAsync(cancellationToken);
        await database.Set<Folder>().Where(folder => folder.SiteId == site).ExecuteDeleteAsync(cancellationToken);

        var removed = 0;
        var left = 0;
        while (true)
        {
            var batch = await database.Set<Asset>().AsNoTracking()
                .Where(asset => asset.SiteId == site)
                .OrderBy(asset => asset.Id)
                .Skip(left)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            foreach (var asset in batch)
            {
                if (await RemoveAsync(asset, cancellationToken))
                {
                    removed++;
                }
                else
                {
                    left++;
                }
            }
        }

        if (removed + left > 0)
        {
            MediaLog.SiteLibraryRemoved(logger, removed, site, left);
        }
    }

    // The variants, then the original, then the row. A file that cannot be deleted leaves the asset, and its variant rows
    // that were not deleted, for the cleanup.
    private async Task<bool> RemoveAsync(Asset asset, CancellationToken cancellationToken)
    {
        if (storages.Find(asset.StorageProvider) is not { } storage)
        {
            MediaLog.SiteFileNotDeleted(logger, asset.StorageProvider, asset.StorageKey, asset.Id, asset.SiteId, null);
            return false;
        }

        var variants = await database.Set<Variant>().AsNoTracking().Where(variant => variant.AssetId == asset.Id).ToListAsync(cancellationToken);
        var kept = false;
        foreach (var variant in variants)
        {
            if (await DeletedAsync(storage, variant.StorageKey, asset, cancellationToken))
            {
                await database.Set<Variant>().Where(item => item.AssetId == variant.AssetId && item.ParamsHash == variant.ParamsHash).ExecuteDeleteAsync(cancellationToken);
            }
            else
            {
                kept = true;
            }
        }

        if (kept || !await DeletedAsync(storage, asset.StorageKey, asset, cancellationToken))
        {
            return false;
        }

        await database.Set<Asset>().Where(item => item.Id == asset.Id).ExecuteDeleteAsync(cancellationToken);
        return true;
    }

    private async Task<bool> DeletedAsync(IFileStorage storage, string key, Asset asset, CancellationToken cancellationToken)
    {
        try
        {
            await storage.DeleteAsync(key, cancellationToken);
            return true;
        }
        // A storage's own timeout is a failure of that file; only the delivery's token stops the run.
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            MediaLog.SiteFileNotDeleted(logger, storage.Name, key, asset.Id, asset.SiteId, exception);
            return false;
        }
    }
}
