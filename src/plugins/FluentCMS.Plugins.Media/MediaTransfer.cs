using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentCMS.Events;
using FluentCMS.Media;
using FluentCMS.Settings;
using FluentCMS.Transfer;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Plugins.Media;

// The media of a site in a transfer package: plugins/media/folders.jsonl, plugins/media/assets.jsonl and, with the option
// mediaFiles=true, the files under plugins/media/files/{id}/. An import keeps the ids, times and authors of the package. An
// asset whose file is neither in the package nor in the storage of the instance, under a key of the site's library that
// the package names, is reported and not imported, so that no entry refers to an asset that cannot be served. A file is never deleted: one that an
// update replaces stays in the storage. The import has checked transfer.manage, so the media permissions are not asked; the
// rules of an upload still hold: every file an asset takes, from the package or at a key of the storage, passes the checks
// of an upload in the site of the import, and its type, size and image size are those the checks find, never its line's.
// The lines are imported as they are read, and a file of the package or a key of the storage is taken by one line of an
// import only, so that what an import reads and writes is never more than the package holds.
internal sealed class MediaTransfer(
    PluginDbContext<MediaPlugin> database,
    MediaLibraries libraries,
    MediaStorage storages,
    ISettings<MediaSettings> settings,
    IEventBus events) : ITransferContributor
{
    public const string FilesOption = "mediaFiles";
    public const string AssetsSection = MediaService.ReferenceTarget;
    public const string FoldersSection = "media/folders";

    private const string Folders = "folders.jsonl";
    private const string Assets = "assets.jsonl";
    private const string FilesFolder = "files/";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly byte[] NewLine = "\n"u8.ToArray();

    public string Name => "media";

    public async Task ExportAsync(ITransferExport context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var library = (await libraries.OfAsync(context.Site, cancellationToken)).Value;
        var withFiles = context.Options.TryGetValue(FilesOption, out var files) && string.Equals(files, "true", StringComparison.OrdinalIgnoreCase);

        var folders = await database.Set<Folder>().AsNoTracking().Where(folder => folder.SiteId == library).ToListAsync(cancellationToken);
        await context.WriteAsync(
            Folders,
            async (stream, token) =>
            {
                foreach (var folder in folders.OrderBy(folder => folder.Path.Length).ThenBy(folder => folder.Path, StringComparer.Ordinal))
                {
                    await WriteLineAsync(stream, new FolderLine(folder.Id, folder.ParentId, folder.Name), token);
                }
            },
            cancellationToken);

        var assets = await database.Set<Asset>().AsNoTracking()
            .Where(asset => asset.SiteId == library && asset.DeletedAt == null)
            .ToListAsync(cancellationToken);
        assets = [.. assets.OrderBy(asset => asset.Id)];
        HashSet<Guid> written = [];
        if (withFiles)
        {
            foreach (var asset in assets)
            {
                if (storages.Find(asset.StorageProvider) is not { } storage)
                {
                    continue;
                }

                await using var content = await storage.OpenReadAsync(asset.StorageKey, cancellationToken);
                if (content is null)
                {
                    continue;
                }

                await context.WriteAsync(FileOf(asset.Id, asset.StorageKey), (stream, token) => content.CopyToAsync(stream, token), cancellationToken);
                written.Add(asset.Id);
            }
        }

        await context.WriteAsync(
            Assets,
            async (stream, token) =>
            {
                foreach (var asset in assets)
                {
                    await WriteLineAsync(stream, AssetLine.Of(asset, written.Contains(asset.Id) ? FileOf(asset.Id, asset.StorageKey) : null), token);
                }
            },
            cancellationToken);
    }

    public async Task ImportAsync(ITransferImport context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var library = (await libraries.OfAsync(context.Site, cancellationToken)).Value;
        var rules = await settings.GetAsync(context.Site, cancellationToken);
        HashSet<Guid> known = [.. await database.Set<Folder>().AsNoTracking().Where(folder => folder.SiteId == library).Select(folder => folder.Id).ToListAsync(cancellationToken)];
        HashSet<string> taken = new(StringComparer.Ordinal);
        await context.ReadAsync(
            Folders,
            (stream, token) => ReadLinesAsync<FolderLine>(stream, context, FoldersSection, folder => ItemAsync(context, FoldersSection, folder.Id, () => ImportFolderAsync(context, library, folder, known, token)), token),
            cancellationToken);
        await context.ReadAsync(
            Assets,
            (stream, token) => ReadLinesAsync<AssetLine>(stream, context, AssetsSection, asset => ItemAsync(context, AssetsSection, asset.Id, () => ImportAssetAsync(context, library, rules, asset, known, taken, token)), token),
            cancellationToken);
    }

    // A folder whose parent is neither in the instance nor imported before it is an error, as is a name its siblings have.
    private async Task<TransferOutcome> ImportFolderAsync(ITransferImport context, Guid library, FolderLine line, HashSet<Guid> known, CancellationToken cancellationToken)
    {
        var existing = await database.Set<Folder>().FirstOrDefaultAsync(folder => folder.Id == line.Id, cancellationToken);
        if (existing is not null && existing.SiteId != library)
        {
            throw Refused(MediaErrorCodes.TransferIdTaken, $"The folder {line.Id:D} is one of another site of this instance.");
        }

        string parentPath = "/";
        if (line.ParentId is { } parentId)
        {
            if (!known.Contains(parentId))
            {
                throw Refused(MediaErrorCodes.FolderNotFound, $"The parent {parentId:D} of the folder {line.Id:D} is neither in the instance nor in the package.");
            }

            parentPath = await database.Set<Folder>().AsNoTracking().Where(folder => folder.Id == parentId).Select(folder => folder.Path).FirstOrDefaultAsync(cancellationToken) ?? $"/{parentId:D}/";
        }

        var path = $"{parentPath}{line.Id:D}/";
        if (existing is not null && existing.Name == line.Name && existing.ParentId == line.ParentId)
        {
            return TransferOutcome.Skipped;
        }

        if (await database.Set<Folder>().AnyAsync(folder => folder.SiteId == library && folder.ParentId == line.ParentId && folder.Name == line.Name && folder.Id != line.Id, cancellationToken))
        {
            throw Refused(MediaErrorCodes.FolderNameTaken, $"A folder named '{line.Name}' is in the same parent in this instance.");
        }

        known.Add(line.Id);
        if (context.DryRun)
        {
            return existing is null ? TransferOutcome.Created : TransferOutcome.Updated;
        }

        if (existing is null)
        {
            database.Add(new Folder { Id = line.Id, SiteId = library, ParentId = line.ParentId, Name = line.Name, Path = path });
        }
        else
        {
            // A folder that moves takes its subtree with it, as a move through the service does.
            var oldPrefix = existing.Path;
            var subtree = await database.Set<Folder>().Where(folder => folder.SiteId == library && folder.Path.StartsWith(oldPrefix) && folder.Id != existing.Id).ToListAsync(cancellationToken);
            foreach (var item in subtree)
            {
                item.Path = path + item.Path[oldPrefix.Length..];
            }

            existing.Name = line.Name;
            existing.ParentId = line.ParentId;
            existing.Path = path;
        }

        await database.SaveChangesAsync(cancellationToken);
        return existing is null ? TransferOutcome.Created : TransferOutcome.Updated;
    }

    private async Task<TransferOutcome> ImportAssetAsync(ITransferImport context, Guid library, MediaSettings rules, AssetLine line, HashSet<Guid> folders, HashSet<string> taken, CancellationToken cancellationToken)
    {
        var existing = await database.Set<Asset>().FirstOrDefaultAsync(asset => asset.Id == line.Id, cancellationToken);
        if (existing is not null && (existing.SiteId != library || existing.DeletedAt is not null))
        {
            throw Refused(MediaErrorCodes.TransferIdTaken, $"The asset {line.Id:D} is one of another site of this instance, or deleted in it.");
        }

        if (line.FolderId is { } folderId && !folders.Contains(folderId))
        {
            throw Refused(MediaErrorCodes.FolderNotFound, $"The folder {folderId:D} of the asset {line.Id:D} is neither in the instance nor in the package.");
        }

        if (existing is not null)
        {
            if (Microseconds(existing.UpdatedAt) == Microseconds(line.UpdatedAt) && existing.Checksum == line.Checksum)
            {
                return TransferOutcome.Skipped;
            }

            if (!context.Overwrite && Microseconds(existing.UpdatedAt) > Microseconds(line.UpdatedAt))
            {
                return TransferOutcome.Conflict;
            }
        }

        var name = MediaService.FileNameOf(line.FileName)
            ?? throw Refused(MediaErrorCodes.InvalidField, $"The asset {line.Id:D} needs a file name of at most {MediaModel.FileNameMaxLength} characters, without control characters.");

        // The file of the package, held to the checksum it was exported with and to the checks of an upload, or the one the
        // instance has.
        await using var file = line.File is { } path ? await PackageFileAsync(context, line.Id, path, name, rules, taken, cancellationToken) : null;
        if (file is not null && !string.Equals(file.Received, line.Checksum, StringComparison.OrdinalIgnoreCase))
        {
            throw Refused(MediaErrorCodes.TransferFileMissing, $"The file of the asset {line.Id:D} in the package does not have the checksum of the asset.");
        }

        var replacesFile = existing is null || existing.Checksum != (file?.Checksum ?? line.Checksum);
        StoredFile stored;
        if (file is not null && replacesFile)
        {
            stored = new StoredFile(storages.Current.Name, $"{library:N}/{line.Id:N}/{(existing is null ? "original" : "original-" + file.Checksum[..16])}{file.Type.Extension}", file);
        }
        else if (existing is not null && !replacesFile)
        {
            // The asset's own file, whose name, type, size and image size stay as the instance has them.
            stored = StoredFile.Of(existing);
        }
        else
        {
            stored = await StoredFileAsync(line, library, name, rules, taken, cancellationToken)
                ?? throw Refused(MediaErrorCodes.TransferFileMissing, $"The file of the asset {line.Id:D} is neither in the package nor in the storage of this instance; export with {FilesOption}=true to bring it.");
        }

        if (context.DryRun)
        {
            return existing is null ? TransferOutcome.Created : TransferOutcome.Updated;
        }

        if (file is not null && replacesFile)
        {
            file.Content.Position = 0;
            await storages.Current.WriteAsync(stored.Key, file.Content, cancellationToken);
        }

        var asset = existing ?? new Asset { Id = line.Id, SiteId = library };
        line.ApplyTo(asset, stored);
        if (existing is null)
        {
            database.Add(asset);
        }

        await database.SaveChangesAsync(cancellationToken);

        MediaAssetEvent raised = existing is null
            ? new MediaAssetUploadedEvent(asset.Id, asset.FolderId, asset.FileName, asset.MimeType, asset.Size)
            : replacesFile
                ? new MediaAssetReplacedEvent(asset.Id, asset.FolderId, asset.FileName, asset.MimeType, asset.Size)
                : new MediaAssetUpdatedEvent(asset.Id, asset.FolderId, asset.FileName);
        await events.PublishAsync(raised, cancellationToken);
        return existing is null ? TransferOutcome.Created : TransferOutcome.Updated;
    }

    // An item the instance refuses is reported, and the others go on. Anything else stops the import, as the contract says.
    private async Task ItemAsync(ITransferImport context, string section, Guid id, Func<Task<TransferOutcome>> import)
    {
        try
        {
            context.Report(section, id.ToString("D"), await import());
        }
        catch (PluginException refused)
        {
            database.ChangeTracker.Clear();
            context.Report(section, id.ToString("D"), TransferOutcome.Error, refused.Code, refused.Message);
        }
        catch (DbUpdateException exception)
        {
            database.ChangeTracker.Clear();
            context.Report(section, id.ToString("D"), TransferOutcome.Error, MediaErrorCodes.TransferIdTaken, $"The database refused the item: {exception.InnerException?.Message ?? exception.Message}");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A storage that refuses a key or a write fails this item only.
            database.ChangeTracker.Clear();
            context.Report(section, id.ToString("D"), TransferOutcome.Error, MediaErrorCodes.TransferFileMissing, $"The file of the item could not be stored: {exception.Message}");
        }
    }

    // Core holds each line of a .jsonl file to FluentCms:Transfer:MaxLineLength as the stream is read.
    private static async Task ReadLinesAsync<T>(Stream stream, ITransferImport context, string section, Func<T, Task> import, CancellationToken cancellationToken)
        where T : class
    {
        using var reader = new StreamReader(stream);
        var number = 0;
        while (await reader.ReadLineAsync(cancellationToken) is { } text)
        {
            number++;
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            T line;
            try
            {
                line = JsonSerializer.Deserialize<T>(text, Json) ?? throw new JsonException("The line is null.");
            }
            catch (JsonException exception)
            {
                context.Report(section, $"line {number.ToString(CultureInfo.InvariantCulture)}", TransferOutcome.Error, MediaErrorCodes.InvalidField, exception.Message);
                continue;
            }

            await import(line);
        }
    }

    // A file of the package or a key of the storage that an earlier line of the import took, whatever became of it.
    private static void Take(HashSet<string> taken, string slot, string file, Guid asset)
    {
        if (!taken.Add(slot))
        {
            throw Refused(MediaErrorCodes.TransferFileMissing, $"The file {file} of the asset {asset:D} is the file of an earlier line of the package; one asset of an import takes a file.");
        }
    }

    // The file of the package through the checks of an upload in the site of the import, with no limit of a request; null
    // when the package does not have it.
    private static async Task<MediaService.Upload?> PackageFileAsync(ITransferImport context, Guid asset, string path, string name, MediaSettings rules, HashSet<string> taken, CancellationToken cancellationToken)
    {
        Take(taken, "package/" + path, path, asset);
        MediaService.Upload? file = null;
        try
        {
            await context.ReadAsync(path, async (stream, token) => file = await MediaService.PrepareAsync(name, stream, rules, long.MaxValue, long.MaxValue, token), cancellationToken);
            return file;
        }
        catch
        {
            if (file is not null)
            {
                await file.DisposeAsync();
            }

            throw;
        }
    }

    // A line without a file may name a key of the site's library in a storage of the instance, as a storage that instances
    // share has. The file there passes the same checks, so that a line cannot give it another type, and must be the file of
    // the line as the checks would store it.
    private async Task<StoredFile?> StoredFileAsync(AssetLine line, Guid library, string name, MediaSettings rules, HashSet<string> taken, CancellationToken cancellationToken)
    {
        if (line.StorageProvider is not { } provider
            || line.StorageKey is not { } key
            || !FileStorageKeys.IsValid(key)
            || !key.StartsWith($"{library:N}/", StringComparison.Ordinal)
            || storages.Find(provider) is not { } storage)
        {
            return null;
        }

        Take(taken, $"storage/{provider}/{key}", key, line.Id);

        await using var content = await storage.OpenReadAsync(key, cancellationToken);
        if (content is null)
        {
            return null;
        }

        await using var file = await MediaService.PrepareAsync(name, content, rules, long.MaxValue, long.MaxValue, cancellationToken);
        if (!string.Equals(file.Received, line.Checksum, StringComparison.OrdinalIgnoreCase) || file.Checksum != file.Received)
        {
            throw Refused(MediaErrorCodes.TransferFileMissing, $"The file at {key} in the storage is not the file of the asset {line.Id:D} as the media settings of the site store it; export with {FilesOption}=true to bring it.");
        }

        return new StoredFile(provider, key, file);
    }

    private static async Task WriteLineAsync<T>(Stream stream, T line, CancellationToken cancellationToken)
    {
        await JsonSerializer.SerializeAsync(stream, line, Json, cancellationToken);
        await stream.WriteAsync(NewLine, cancellationToken);
    }

    private static string FileOf(Guid id, string storageKey) => $"{FilesFolder}{id:N}/original{Extension(storageKey)}";

    // Only an extension that is a storage key's segment as it is, so that a package cannot name a key the storage refuses.
    private static string Extension(string path)
    {
        var name = path[(path.LastIndexOf('/') + 1)..];
        var dot = name.LastIndexOf('.');
        var extension = dot >= 0 ? name[dot..] : "";
        return extension.Length is > 1 and <= 16 && extension[1..].All(char.IsAsciiLetterOrDigit) ? extension : "";
    }

    private static long Microseconds(DateTimeOffset moment) => moment.UtcTicks / 10;

    private static PluginException Refused(string code, string message) => new(code, message);

    private sealed record FolderLine(Guid Id, Guid? ParentId, string Name);

    private sealed record AssetLine(
        Guid Id,
        Guid? FolderId,
        string FileName,
        string MimeType,
        long Size,
        string Checksum,
        int? Width,
        int? Height,
        double? Duration,
        string? Title,
        JsonElement Alt,
        JsonElement Caption,
        string? Credit,
        JsonElement Tags,
        double? FocalX,
        double? FocalY,
        bool IsPrivate,
        DateTimeOffset CreatedAt,
        Guid CreatedBy,
        DateTimeOffset UpdatedAt,
        Guid UpdatedBy,
        string? StorageProvider,
        string? StorageKey,
        string? File)
    {
        public static AssetLine Of(Asset asset, string? file) => new(
            asset.Id,
            asset.FolderId,
            asset.FileName,
            asset.MimeType,
            asset.Size,
            asset.Checksum,
            asset.Width,
            asset.Height,
            asset.Duration,
            asset.Title,
            JsonDocument.Parse(asset.Alt).RootElement.Clone(),
            JsonDocument.Parse(asset.Caption).RootElement.Clone(),
            asset.Credit,
            JsonDocument.Parse(asset.Tags).RootElement.Clone(),
            asset.FocalX,
            asset.FocalY,
            asset.IsPrivate,
            asset.CreatedAt,
            asset.CreatedBy,
            asset.UpdatedAt,
            asset.UpdatedBy,
            asset.StorageProvider,
            asset.StorageKey,
            file);

        // The columns of the file are those of the file the asset takes, and the others the line's.
        public void ApplyTo(Asset asset, StoredFile file)
        {
            asset.FolderId = FolderId;
            asset.FileName = file.FileName;
            asset.StorageProvider = file.Provider;
            asset.StorageKey = file.Key;
            asset.MimeType = file.MimeType;
            asset.Size = file.Size;
            asset.Checksum = file.Checksum;
            asset.Width = file.Width;
            asset.Height = file.Height;
            asset.Duration = Duration;
            asset.Title = Title;
            asset.Alt = Alt.ValueKind == JsonValueKind.Object ? Alt.GetRawText() : "{}";
            asset.Caption = Caption.ValueKind == JsonValueKind.Object ? Caption.GetRawText() : "{}";
            asset.Credit = Credit;
            asset.Tags = Tags.ValueKind == JsonValueKind.Array ? Tags.GetRawText() : "[]";
            asset.FocalX = FocalX;
            asset.FocalY = FocalY;
            asset.IsPrivate = IsPrivate;
            asset.CreatedAt = CreatedAt;
            asset.CreatedBy = CreatedBy;
            asset.UpdatedAt = UpdatedAt;
            asset.UpdatedBy = UpdatedBy;
        }
    }

    // The file an asset takes, where it is stored and what is known of it.
    private sealed record StoredFile(string FileName, string Provider, string Key, string MimeType, long Size, string Checksum, int? Width, int? Height)
    {
        public StoredFile(string provider, string key, MediaService.Upload file)
            : this(file.FileName, provider, key, file.Type.MimeType, file.Size, file.Checksum, file.Dimensions?.Width, file.Dimensions?.Height)
        {
        }

        public static StoredFile Of(Asset asset) =>
            new(asset.FileName, asset.StorageProvider, asset.StorageKey, asset.MimeType, asset.Size, asset.Checksum, asset.Width, asset.Height);
    }
}
