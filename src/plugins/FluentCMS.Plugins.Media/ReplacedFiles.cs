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
// names them, as for an owner deleted first. A line of an import can name one of the keys after that check and before the
// save, holding the asset's version as it was, which the save does not change; so the check is made again after the save,
// just before the files are deleted, and a file named by then goes to a holder too. The variant rows of an asset that does
// not own its file go to the asset that does, as when the asset is deleted (HandOverAsync).
internal sealed class ReplacedFiles
{
    private readonly Asset _asset;
    private readonly List<string> _keys;
    private readonly List<Variant> _variants;
    private readonly Asset? _holder;

    private ReplacedFiles(Asset asset, List<string> keys, List<Variant> variants, Asset? holder) =>
        (_asset, _keys, _variants, _holder) = (asset, keys, variants, holder);

    // Before the asset takes its new file: removes its variant rows from the context, to go with the save. Without
    // deleteFiles only the rows go, as for a key a line of an import named, whose files may be another instance's.
    public static async Task<ReplacedFiles> TakeAsync(PluginDbContext<MediaPlugin> database, Asset asset, bool deleteFiles, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var variants = await database.Set<Variant>().Where(variant => variant.AssetId == asset.Id).ToListAsync(cancellationToken);
        database.RemoveRange(variants);
        var before = Copy(asset);
        List<Variant> copies = [.. variants.Select(variant => Copy(variant, variant.AssetId))];
        if (!deleteFiles)
        {
            return new ReplacedFiles(before, [], copies, null);
        }

        List<string> keys = [asset.StorageKey, .. variants.Select(variant => variant.StorageKey)];
        var holder = HolderOf(asset, now);
        if (!await NamedByOthers(database, asset, keys).AnyAsync(cancellationToken))
        {
            return new ReplacedFiles(before, keys, copies, holder);
        }

        database.Add(holder);
        database.AddRange(variants.Select(variant => Copy(variant, holder.Id)));
        return new ReplacedFiles(before, [], copies, null);
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
    // files with its own. Without such an asset only the rows go, since the files can be another instance's. Each row goes on
    // its own, so that one the owner made at the same moment, which beats the save, is held all the same, and one whose save
    // fails otherwise stays with the asset for the next run. Answers the variants whose rows may go; it removes none.
    internal static async Task<List<Variant>> HandOverAsync(PluginDbContext<MediaPlugin> database, Asset named, IReadOnlyList<Variant> variants, CancellationToken cancellationToken)
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
        if (owner is not { } id)
        {
            return [.. variants];
        }

        List<Variant> handed = [];
        foreach (var variant in variants)
        {
            if (await HeldAsync(database, id, variant.ParamsHash, cancellationToken))
            {
                handed.Add(variant);
                continue;
            }

            var row = Copy(variant, id);
            database.Add(row);
            var saved = false;
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                saved = true;
            }
            catch (DbUpdateException)
            {
            }
            finally
            {
                database.Entry(row).State = EntityState.Detached;
            }

            if (saved || await HeldAsync(database, id, variant.ParamsHash, cancellationToken))
            {
                handed.Add(variant);
            }
        }

        return handed;
    }

    // After the save, with the asset as saved. The rows of an asset that does not own its file are handed over; the files
    // of one that does are deleted, unless an asset names one of their keys by now.
    public async Task DeleteAsync(PluginDbContext<MediaPlugin> database, Asset saved, MediaStorage storages, ILogger logger)
    {
        if (_holder is null && _keys.Count == 0)
        {
            if (!_asset.OwnsFile && _variants.Count > 0)
            {
                await HandOverAsync(database, _asset, _variants, CancellationToken.None);
            }

            return;
        }

        var keys = saved.StorageProvider == _asset.StorageProvider
            ? _keys.Where(key => !string.Equals(key, saved.StorageKey, StringComparison.OrdinalIgnoreCase)).ToList()
            : _keys;
        if (keys.Count == 0)
        {
            return;
        }

        if (_holder is not null && await NamedByOthers(database, saved, keys).AnyAsync(CancellationToken.None))
        {
            database.Add(_holder);
            List<Variant> rows = [.. _variants.Select(variant => Copy(variant, _holder.Id))];
            database.AddRange(rows);
            try
            {
                await database.SaveChangesAsync(CancellationToken.None);
            }
            catch (DbUpdateException exception)
            {
                // The files stay, with no row that owns them, rather than go while an asset names them.
                foreach (var key in keys)
                {
                    MediaLog.ReplacedNotDeleted(logger, _asset.Id, _asset.StorageProvider, key, exception);
                }
            }
            finally
            {
                database.Entry(_holder).State = EntityState.Detached;
                foreach (var row in rows)
                {
                    database.Entry(row).State = EntityState.Detached;
                }
            }

            return;
        }

        if (storages.Find(_asset.StorageProvider) is not { } storage)
        {
            foreach (var key in keys)
            {
                MediaLog.ReplacedNotDeleted(logger, _asset.Id, _asset.StorageProvider, key, null);
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
                MediaLog.ReplacedNotDeleted(logger, _asset.Id, _asset.StorageProvider, key, exception);
            }
        }
    }

    private static Task<bool> HeldAsync(PluginDbContext<MediaPlugin> database, Guid asset, string hash, CancellationToken cancellationToken) =>
        database.Set<Variant>().AsNoTracking().AnyAsync(variant => variant.AssetId == asset && variant.ParamsHash == hash, cancellationToken);

    private static Variant Copy(Variant variant, Guid asset) =>
        new() { AssetId = asset, ParamsHash = variant.ParamsHash, StorageKey = variant.StorageKey, Size = variant.Size, CreatedAt = variant.CreatedAt };

    // The columns of the file of an asset as read, before a replace changes them.
    private static Asset Copy(Asset asset) => new()
    {
        Id = asset.Id,
        SiteId = asset.SiteId,
        StorageProvider = asset.StorageProvider,
        StorageKey = asset.StorageKey,
        OwnsFile = asset.OwnsFile,
        Checksum = asset.Checksum,
    };

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
