using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins.Media;

// The files an asset leaves when its file is replaced, through the service or by an import: the old original and the
// variants. The variant rows go with the save of the asset, and the files after it, from the storage the asset recorded, so
// that the old file serves until the row names the new one. A file that another asset of the library names, as a line of an
// import can name the key of another asset, stays with the variants, which share the keys of the same file; so does a file
// that cannot be deleted, which is logged, and the replace stands.
internal sealed class ReplacedFiles
{
    private readonly Guid _asset;
    private readonly string _provider;
    private readonly List<string> _keys;

    private ReplacedFiles(Guid asset, string provider, List<string> keys) => (_asset, _provider, _keys) = (asset, provider, keys);

    // Before the asset takes its new file: removes its variant rows from the context, to go with the save. Without
    // deleteFiles only the rows go, as for a key a line of an import named, whose files may be another instance's.
    public static async Task<ReplacedFiles> TakeAsync(PluginDbContext<MediaPlugin> database, Asset asset, bool deleteFiles, CancellationToken cancellationToken)
    {
        var variants = await database.Set<Variant>().Where(variant => variant.AssetId == asset.Id).ToListAsync(cancellationToken);
        database.RemoveRange(variants);
        if (!deleteFiles)
        {
            return new ReplacedFiles(asset.Id, asset.StorageProvider, []);
        }

        List<string> keys = [asset.StorageKey, .. variants.Select(variant => variant.StorageKey)];

        var named = await OthersNaming(database, asset, keys).AnyAsync(cancellationToken);
        return new ReplacedFiles(asset.Id, asset.StorageProvider, named ? [] : keys);
    }

    // The other assets of the library that name one of the keys. Keys are compared without regard to case, as a disk that
    // ignores it reads them; they are ASCII, so lower() is the same everywhere.
    internal static IQueryable<Asset> OthersNaming(PluginDbContext<MediaPlugin> database, Asset asset, IEnumerable<string> keys)
    {
        var lowered = keys.Select(key => key.ToLowerInvariant()).ToList();
#pragma warning disable CA1304, CA1311 // The database lowers the column, where lower() takes no culture.
        return database.Set<Asset>().Where(other => other.SiteId == asset.SiteId && other.Id != asset.Id && lowered.Contains(other.StorageKey.ToLower()));
#pragma warning restore CA1304, CA1311
    }

    // After the save.
    public async Task DeleteAsync(MediaStorage storages, ILogger logger)
    {
        if (_keys.Count == 0)
        {
            return;
        }

        if (storages.Find(_provider) is not { } storage)
        {
            foreach (var key in _keys)
            {
                MediaLog.ReplacedNotDeleted(logger, _asset, _provider, key, null);
            }

            return;
        }

        foreach (var key in _keys)
        {
            try
            {
                await storage.DeleteAsync(key, CancellationToken.None);
            }
            catch (Exception exception)
            {
                MediaLog.ReplacedNotDeleted(logger, _asset, _provider, key, exception);
            }
        }
    }
}
