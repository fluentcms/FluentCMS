using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins.Media;

// The files an asset leaves when its file is replaced, through the service or by an import: the old original and the
// variants. The variant rows go with the save of the asset, and the files after it, from the storage the asset recorded, so
// that the old file serves until the row names the new one. A file that another asset of the library names, as a line of an
// import can name the key of another asset, stays with the variants, which share the keys of the same file; so does the
// key the saved row names, which a line can take from the old ones, and a file that cannot be deleted, which is logged, and
// the replace stands. Files that stay because another asset names them go to a holder in the same save: a deleted row of
// their own, which owns the old original and takes the variant rows, so that the purge deletes them once no other asset
// names them, as for an owner deleted first.
internal sealed class ReplacedFiles
{
    private readonly Guid _asset;
    private readonly string _provider;
    private readonly List<string> _keys;

    private ReplacedFiles(Guid asset, string provider, List<string> keys) => (_asset, _provider, _keys) = (asset, provider, keys);

    // Before the asset takes its new file: removes its variant rows from the context, to go with the save. Without
    // deleteFiles only the rows go, as for a key a line of an import named, whose files may be another instance's.
    public static async Task<ReplacedFiles> TakeAsync(PluginDbContext<MediaPlugin> database, Asset asset, bool deleteFiles, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var variants = await database.Set<Variant>().Where(variant => variant.AssetId == asset.Id).ToListAsync(cancellationToken);
        database.RemoveRange(variants);
        if (!deleteFiles)
        {
            return new ReplacedFiles(asset.Id, asset.StorageProvider, []);
        }

        List<string> keys = [asset.StorageKey, .. variants.Select(variant => variant.StorageKey)];
        if (!await NamedByOthers(database, asset, keys).AnyAsync(cancellationToken))
        {
            return new ReplacedFiles(asset.Id, asset.StorageProvider, keys);
        }

        var holder = HolderOf(asset, now);
        database.Add(holder);
        database.AddRange(variants.Select(variant => new Variant { AssetId = holder.Id, ParamsHash = variant.ParamsHash, StorageKey = variant.StorageKey, Size = variant.Size, CreatedAt = variant.CreatedAt }));
        return new ReplacedFiles(asset.Id, asset.StorageProvider, []);
    }

    // The keys that other assets of the library name, as their file or a variant's. Only an asset whose key a line named can:
    // every key the instance writes is in its asset's folder under a name of its own, and a variant goes in the folder of its
    // asset's key, so an asset that names another's key, with the same bytes, makes variants at the other's keys. A holder
    // names keys in the folder of the asset it was split from, which makes its variants at the same keys again when it holds
    // the same bytes again, so that asset counts as another too. Keys are compared without regard to case, as a disk that
    // ignores it reads them; they are ASCII, so lower() is the same everywhere.
    internal static IQueryable<string> NamedByOthers(PluginDbContext<MediaPlugin> database, Asset asset, IEnumerable<string> keys)
    {
        var lowered = keys.Select(key => key.ToLowerInvariant()).ToList();
        var named = Naming(database, database.Set<Asset>().Where(other => other.SiteId == asset.SiteId && !other.OwnsFile && other.Id != asset.Id), lowered);
        var folders = lowered.Select(FolderOf).OfType<Guid>().Where(folder => folder != asset.Id).Distinct().ToList();
        return folders.Count == 0
            ? named
            : named.Concat(Naming(database, database.Set<Asset>().Where(other => other.SiteId == asset.SiteId && other.OwnsFile && folders.Contains(other.Id)), lowered));
    }

    // For the variants of an asset that is gone, whose site and owner are not known: the keys that any asset names, as its
    // file or a variant's of another asset.
    internal static IQueryable<string> NamedByAny(PluginDbContext<MediaPlugin> database, Guid gone, IEnumerable<string> keys)
    {
        var lowered = keys.Select(key => key.ToLowerInvariant()).ToList();
#pragma warning disable CA1304, CA1311 // The database lowers the column, where lower() takes no culture.
        return database.Set<Asset>().Where(asset => lowered.Contains(asset.StorageKey.ToLower())).Select(asset => asset.StorageKey)
            .Concat(database.Set<Variant>().Where(variant => variant.AssetId != gone && lowered.Contains(variant.StorageKey.ToLower())).Select(variant => variant.StorageKey));
#pragma warning restore CA1304, CA1311
    }

    // The asset whose folder a key is in, {library}/{asset}/{name}, as every key the instance writes is.
    internal static Guid? FolderOf(string key)
    {
        var segments = key.Split('/');
        return segments.Length > 2 && Guid.TryParseExact(segments[1], "N", out var asset) ? asset : null;
    }

    // The variant rows of an asset that does not own its file, which is going. They go to the asset of the library that owns
    // the file at its key and holds the same bytes, which makes those variants at the same keys, so that its owner deletes the
    // files with its own. Without such an asset only the rows go, since the files can be another instance's, and so they do
    // when the owner makes one of those variants itself at the same moment, whose row then beats the save. Answers the rows
    // removed.
    internal static async Task<int> HandOverAsync(PluginDbContext<MediaPlugin> database, Asset named, IReadOnlyList<Variant> variants, CancellationToken cancellationToken)
    {
        // The asset of the key's folder, by its id, and only when that is not it, as after a replace, a holder of its old files.
        var lowered = named.StorageKey.ToLowerInvariant();
        var folder = FolderOf(lowered);
#pragma warning disable CA1304, CA1311, CA1862 // The database lowers the column, where lower() takes no culture.
        var owners = database.Set<Asset>().AsNoTracking()
            .Where(other => other.SiteId == named.SiteId && other.OwnsFile && other.Id != named.Id && other.StorageProvider == named.StorageProvider && other.Checksum == named.Checksum && other.StorageKey.ToLower() == lowered);
        var owner = await owners.Where(other => other.Id == folder).Select(other => (Guid?)other.Id).FirstOrDefaultAsync(cancellationToken)
            ?? await owners.Where(other => other.DeletedAt != null).OrderBy(other => other.Id).Select(other => (Guid?)other.Id).FirstOrDefaultAsync(cancellationToken);
#pragma warning restore CA1304, CA1311, CA1862
        if (owner is { } id)
        {
            var held = await database.Set<Variant>().AsNoTracking().Where(variant => variant.AssetId == id).Select(variant => variant.ParamsHash).ToListAsync(cancellationToken);
            database.AddRange(variants.Where(variant => !held.Contains(variant.ParamsHash)).Select(variant => new Variant { AssetId = id, ParamsHash = variant.ParamsHash, StorageKey = variant.StorageKey, Size = variant.Size, CreatedAt = variant.CreatedAt }));
            try
            {
                await database.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
            }
            finally
            {
                database.ChangeTracker.Clear();
            }
        }

        var hashes = variants.Select(variant => variant.ParamsHash).ToList();
        return await database.Set<Variant>().Where(variant => variant.AssetId == named.Id && hashes.Contains(variant.ParamsHash)).ExecuteDeleteAsync(cancellationToken);
    }

    // After the save, with the asset as saved.
    public async Task DeleteAsync(Asset saved, MediaStorage storages, ILogger logger)
    {
        var keys = saved.StorageProvider == _provider
            ? _keys.Where(key => !string.Equals(key, saved.StorageKey, StringComparison.OrdinalIgnoreCase)).ToList()
            : _keys;
        if (keys.Count == 0)
        {
            return;
        }

        if (storages.Find(_provider) is not { } storage)
        {
            foreach (var key in keys)
            {
                MediaLog.ReplacedNotDeleted(logger, _asset, _provider, key, null);
            }

            return;
        }

        foreach (var key in keys)
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

    private static IQueryable<string> Naming(PluginDbContext<MediaPlugin> database, IQueryable<Asset> others, List<string> lowered)
    {
#pragma warning disable CA1304, CA1311 // The database lowers the column, where lower() takes no culture.
        return others.Where(other => lowered.Contains(other.StorageKey.ToLower())).Select(other => other.StorageKey)
            .Concat(database.Set<Variant>().Where(variant => others.Select(other => other.Id).Contains(variant.AssetId) && lowered.Contains(variant.StorageKey.ToLower())).Select(variant => variant.StorageKey));
#pragma warning restore CA1304, CA1311
    }

    // The old file of an asset, kept for another asset that names it: a deleted row that owns it, with no folder, metadata or
    // references, which no package names and nothing serves.
    private static Asset HolderOf(Asset asset, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        SiteId = asset.SiteId,
        FileName = asset.FileName,
        StorageProvider = asset.StorageProvider,
        StorageKey = asset.StorageKey,
        OwnsFile = true,
        MimeType = asset.MimeType,
        Size = asset.Size,
        Checksum = asset.Checksum,
        Width = asset.Width,
        Height = asset.Height,
        Duration = asset.Duration,
        IsPrivate = true,
        CreatedAt = asset.CreatedAt,
        CreatedBy = asset.CreatedBy,
        UpdatedAt = now,
        UpdatedBy = asset.UpdatedBy,
        DeletedAt = now,
    };
}
