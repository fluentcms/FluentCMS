using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentCMS.Events;
using FluentCMS.Media;
using FluentCMS.Settings;
using FluentCMS.Transfer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins.Media;

// The media of a site in a transfer package: plugins/media/folders.jsonl, plugins/media/assets.jsonl and, with the option
// mediaFiles=true, the files under plugins/media/files/{id}/. An import keeps the ids, times and authors of the package. An
// asset whose file is neither in the package nor in the storage of the instance, under a key of the site's library that
// the package names, is reported and not imported, so that no entry refers to an asset that cannot be served. A file that an
// update replaces stays in the storage; only a file the import wrote for a row it then did not save is deleted again, as for
// an upload. The import has checked transfer.manage, so the media permissions are not asked; the rules of an upload still hold: every file an asset takes, from the package or at a key of the storage, passes the checks
// of an upload in the site of the import, and its type, size and image size are those the checks find, never its line's.
// The fields of a line and a folder's place are held to what the service allows, so that the database refuses none of them.
// The lines are imported as they are read, and a file of the package or a key of the storage is taken by one line of an
// import only, so that what an import reads and writes is never more than the package holds.
internal sealed class MediaTransfer(
    PluginDbContext<MediaPlugin> database,
    MediaLibraries libraries,
    MediaStorage storages,
    ISettings<MediaSettings> settings,
    IEventBus events,
    ILogger<MediaTransfer> logger) : ITransferContributor
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

        // The path of each folder of the library as the import leaves it, a dry run too, so that a line is checked against the
        // lines before it.
        var folders = await database.Set<Folder>().AsNoTracking().Where(folder => folder.SiteId == library).ToDictionaryAsync(folder => folder.Id, folder => folder.Path, cancellationToken);

        // A disk that ignores case reads ORIGINAL.png and original.png as one file.
        HashSet<string> taken = new(StringComparer.OrdinalIgnoreCase);
        await context.ReadAsync(
            Folders,
            (stream, token) => ReadLinesAsync<FolderLine>(stream, context, FoldersSection, folder => ItemAsync(context, FoldersSection, folder.Id, () => ImportFolderAsync(context, library, folder, folders, token)), token),
            cancellationToken);
        await context.ReadAsync(
            Assets,
            (stream, token) => ReadLinesAsync<AssetLine>(stream, context, AssetsSection, asset => ItemAsync(context, AssetsSection, asset.Id, () => ImportAssetAsync(context, library, rules, asset, folders, taken, token)), token),
            cancellationToken);
    }

    // A folder line is held to what a create, a rename and a move through the service allow: a name the service takes, a
    // parent in the instance or imported before it, never itself or a folder below it, folders no deeper than they nest, and
    // a name its siblings do not have.
    private async Task<TransferOutcome> ImportFolderAsync(ITransferImport context, Guid library, FolderLine line, Dictionary<Guid, string> folders, CancellationToken cancellationToken)
    {
        if (line.Id == Guid.Empty)
        {
            throw Refused(MediaErrorCodes.InvalidField, "A folder line needs its id.");
        }

        var name = MediaService.FolderNameOf(line.Name)
            ?? throw Refused(MediaErrorCodes.InvalidField, $"The folder {line.Id:D} has a name the service refuses. {MediaService.FolderNameRule}");
        var existing = await DatabaseAsync(() => database.Set<Folder>().FirstOrDefaultAsync(folder => folder.Id == line.Id, cancellationToken));
        if (existing is not null && existing.SiteId != library)
        {
            throw Refused(MediaErrorCodes.TransferIdTaken, $"The folder {line.Id:D} is one of another site of this instance.");
        }

        if (line.ParentId == line.Id)
        {
            throw Refused(MediaErrorCodes.FolderCycle, $"The folder {line.Id:D} cannot be its own parent.");
        }

        var parentPath = "/";
        if (line.ParentId is { } parentId)
        {
            parentPath = folders.GetValueOrDefault(parentId)
                ?? throw Refused(MediaErrorCodes.FolderNotFound, $"The parent {parentId:D} of the folder {line.Id:D} is neither in the instance nor in the package.");
        }

        if (existing is not null && existing.Name == name && existing.ParentId == line.ParentId)
        {
            return TransferOutcome.Skipped;
        }

        var oldPath = folders.GetValueOrDefault(line.Id);
        if (oldPath is not null && parentPath.StartsWith(oldPath, StringComparison.Ordinal))
        {
            throw Refused(MediaErrorCodes.FolderCycle, $"The folder {line.Id:D} cannot move into itself or one of its subfolders.");
        }

        var path = $"{parentPath}{line.Id:D}/";
        var below = oldPath is null ? 0 : folders.Values.Where(item => item.StartsWith(oldPath, StringComparison.Ordinal)).Max(MediaService.Depth) - MediaService.Depth(oldPath);
        if (MediaService.Depth(path) + below > MediaModel.MaxDepth)
        {
            throw Refused(MediaErrorCodes.InvalidField, $"Folders nest at most {MediaModel.MaxDepth} deep, and the folder {line.Id:D} would take them deeper.");
        }

        if (await DatabaseAsync(() => database.Set<Folder>().AnyAsync(folder => folder.SiteId == library && folder.ParentId == line.ParentId && folder.Name == name && folder.Id != line.Id, cancellationToken)))
        {
            throw Refused(MediaErrorCodes.FolderNameTaken, $"A folder named '{name}' is in the same parent in this instance.");
        }

        if (!context.DryRun)
        {
            if (existing is null)
            {
                database.Add(new Folder { Id = line.Id, SiteId = library, ParentId = line.ParentId, Name = name, Path = path });
            }
            else
            {
                // A folder that moves takes its subtree with it, as a move through the service does.
                var oldPrefix = existing.Path;
                var subtree = await DatabaseAsync(() => database.Set<Folder>().Where(folder => folder.SiteId == library && folder.Path.StartsWith(oldPrefix) && folder.Id != existing.Id).ToListAsync(cancellationToken));
                foreach (var item in subtree)
                {
                    item.Path = path + item.Path[oldPrefix.Length..];
                }

                existing.Name = name;
                existing.ParentId = line.ParentId;
                existing.Path = path;
            }

            await SaveAsync(cancellationToken);
        }

        Move(folders, line.Id, oldPath, path);
        return existing is null ? TransferOutcome.Created : TransferOutcome.Updated;
    }

    // The paths of a folder and of the folders below it, as its move rewrites them.
    private static void Move(Dictionary<Guid, string> folders, Guid id, string? oldPath, string path)
    {
        if (oldPath is null)
        {
            folders[id] = path;
            return;
        }

        foreach (var (folder, at) in folders.Where(item => item.Value.StartsWith(oldPath, StringComparison.Ordinal)).ToList())
        {
            folders[folder] = path + at[oldPath.Length..];
        }
    }

    private async Task<TransferOutcome> ImportAssetAsync(ITransferImport context, Guid library, MediaSettings rules, AssetLine line, Dictionary<Guid, string> folders, HashSet<string> taken, CancellationToken cancellationToken)
    {
        var fields = Checked(line);
        var existing = await DatabaseAsync(() => database.Set<Asset>().FirstOrDefaultAsync(asset => asset.Id == line.Id, cancellationToken));
        if (existing is not null && (existing.SiteId != library || existing.DeletedAt is not null))
        {
            throw Refused(MediaErrorCodes.TransferIdTaken, $"The asset {line.Id:D} is one of another site of this instance, or deleted in it.");
        }

        if (line.FolderId is { } folderId && !folders.ContainsKey(folderId))
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

        // The file of the package, held to the checksum it was exported with and to the checks of an upload, or the one the
        // instance has. A line whose checksum is the asset's keeps the asset's file, so its file in the package is not read,
        // and rules the site has tightened since the asset was stored do not refuse it.
        var keepsFile = existing is not null && existing.Checksum == line.Checksum;
        await using var file = !keepsFile && line.File is { } path ? await PackageFileAsync(context, line.Id, path, fields.FileName, rules, taken, cancellationToken) : null;
        if (file is not null && !string.Equals(file.Received, line.Checksum, StringComparison.OrdinalIgnoreCase))
        {
            throw Refused(MediaErrorCodes.TransferFileMissing, $"The file of the asset {line.Id:D} in the package does not have the checksum of the asset.");
        }

        var replacesFile = existing is null || existing.Checksum != (file?.Checksum ?? line.Checksum);
        StoredFile stored;
        if (file is not null && replacesFile)
        {
            // A key of this write's own, as a replace through the service has, so that the deletion of a file whose row was
            // not saved never takes one that another import, of another site of a shared library, wrote for its own row.
            stored = new StoredFile(storages.Current.Name, $"{library:N}/{line.Id:N}/original-{Guid.CreateVersion7():N}{file.Type.Extension}", file);
        }
        else if (existing is not null && !replacesFile)
        {
            // The asset's own file, whose name, type, size, image size and duration stay as the instance has them.
            stored = StoredFile.Of(existing);
        }
        else
        {
            stored = await StoredFileAsync(line, library, fields.FileName, rules, taken, cancellationToken)
                ?? throw Refused(MediaErrorCodes.TransferFileMissing, $"The file of the asset {line.Id:D} is neither in the package nor in the storage of this instance; export with {FilesOption}=true to bring it.");
        }

        if (context.DryRun)
        {
            return existing is null ? TransferOutcome.Created : TransferOutcome.Updated;
        }

        // A file the import wrote is deleted again when its row is not saved, as for an upload.
        string? written = null;
        if (file is not null && replacesFile)
        {
            file.Content.Position = 0;
            await storages.Current.WriteAsync(stored.Key, file.Content, cancellationToken);
            written = stored.Key;
        }

        var asset = existing ?? new Asset { Id = line.Id, SiteId = library };
        line.ApplyTo(asset, stored, fields);
        if (existing is null)
        {
            database.Add(asset);
        }

        try
        {
            await SaveAsync(cancellationToken);
        }
        catch
        {
            if (written is not null)
            {
                await ForgetAsync(written);
            }

            throw;
        }

        MediaAssetEvent raised = existing is null
            ? new MediaAssetUploadedEvent(asset.Id, asset.FolderId, asset.FileName, asset.MimeType, asset.Size)
            : replacesFile
                ? new MediaAssetReplacedEvent(asset.Id, asset.FolderId, asset.FileName, asset.MimeType, asset.Size)
                : new MediaAssetUpdatedEvent(asset.Id, asset.FolderId, asset.FileName);
        await events.PublishAsync(raised, cancellationToken);
        return existing is null ? TransferOutcome.Created : TransferOutcome.Updated;
    }

    // An item the instance refuses is reported, and the others go on. The checks of a line leave a read or a save of the
    // database that fails, as when another writer took the id at the same time (FCMS3221), and a read or a write of the
    // storage that fails (FCMS3220); their messages, which can name the server's paths, go to the log only. Report throws
    // again a bound of the package that a read reached.
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
            context.Report(section, id.ToString("D"), TransferOutcome.Error, MediaErrorCodes.TransferIdTaken, "The database did not save the item, as when another writer took its id at the same time; the log of the instance has the reason.");
            MediaLog.TransferItemFailed(logger, exception, MediaErrorCodes.TransferIdTaken, section, id);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            database.ChangeTracker.Clear();
            context.Report(section, id.ToString("D"), TransferOutcome.Error, MediaErrorCodes.TransferFileMissing, "The file of the item could not be read or stored; the log of the instance has the reason.");
            MediaLog.TransferItemFailed(logger, exception, MediaErrorCodes.TransferFileMissing, section, id);
        }
    }

    private Task<int> SaveAsync(CancellationToken cancellationToken) => DatabaseAsync(() => database.SaveChangesAsync(cancellationToken));

    // Any failure of a read or of the save of an item is one of the database, so that the item is reported by the step that
    // failed.
    private static async Task<T> DatabaseAsync<T>(Func<Task<T>> work)
    {
        try
        {
            return await work();
        }
        catch (Exception exception) when (exception is not DbUpdateException and not OperationCanceledException)
        {
            throw new DbUpdateException("The database failed on the media rows of the item.", exception);
        }
    }

    private async Task ForgetAsync(string key)
    {
        var storage = storages.Current;
        try
        {
            await storage.DeleteAsync(key, CancellationToken.None);
        }
        catch (Exception exception)
        {
            MediaLog.NotDeleted(logger, storage.Name, key, exception);
        }
    }

    // The fields of a line as an upload and an update through the service hold them, so that the database refuses none of
    // them and a dry run reports what the import does.
    private static AssetFields Checked(AssetLine line)
    {
        if (line.Id == Guid.Empty)
        {
            throw Refused(MediaErrorCodes.InvalidField, "An asset line needs its id.");
        }

        var name = MediaService.FileNameOf(line.FileName)
            ?? throw Refused(MediaErrorCodes.InvalidField, $"The asset {line.Id:D} needs a file name of at most {MediaModel.FileNameMaxLength} characters, without control characters.");
        if (string.IsNullOrEmpty(line.Checksum))
        {
            throw Refused(MediaErrorCodes.InvalidField, $"The asset {line.Id:D} needs the checksum of its file.");
        }

        CheckText(line.Id, "title", line.Title, MediaModel.TitleMaxLength);
        CheckText(line.Id, "credit", line.Credit, MediaModel.CreditMaxLength);
        if ((line.FocalX is null) != (line.FocalY is null) || (line.FocalX is { } x && x is not (>= 0 and <= 1)) || (line.FocalY is { } y && y is not (>= 0 and <= 1)))
        {
            throw Refused(MediaErrorCodes.InvalidField, $"The focal point of the asset {line.Id:D} has x and y from 0 to 1.");
        }

        return new AssetFields(name, TextsOf(line.Id, "alt", line.Alt, MediaService.AltMaxLength), TextsOf(line.Id, "caption", line.Caption, MediaService.CaptionMaxLength), TagsOf(line.Id, line.Tags));
    }

    private static void CheckText(Guid id, string field, string? value, int maxLength)
    {
        if (value?.Length > maxLength)
        {
            throw Refused(MediaErrorCodes.InvalidField, $"The {field} of the asset {id:D} has at most {maxLength} characters.");
        }
    }

    // A text of each locale, as the service reads the column: no other value, and no locale twice in any case.
    private static string TextsOf(Guid id, string field, JsonElement value, int maxLength)
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return "{}";
        }

        HashSet<string> locales = new(StringComparer.OrdinalIgnoreCase);
        return value.ValueKind == JsonValueKind.Object && value.EnumerateObject().All(text => text.Value.ValueKind == JsonValueKind.String && text.Value.GetString()!.Length <= maxLength && locales.Add(text.Name))
            ? value.GetRawText()
            : throw Refused(MediaErrorCodes.InvalidField, $"The {field} of the asset {id:D} is an object of a text of at most {maxLength} characters for each locale.");
    }

    // Written as the service writes them, once each, so that a list filters by a tag of an imported asset too.
    private static string TagsOf(Guid id, JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return "[]";
        }

        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > MediaService.MaxTags)
        {
            throw Refused(MediaErrorCodes.InvalidField, $"The tags of the asset {id:D} are a list of at most {MediaService.MaxTags}.");
        }

        List<string> tags = [];
        foreach (var tag in value.EnumerateArray())
        {
            tags.Add((tag.ValueKind == JsonValueKind.String ? MediaService.TagOf(tag.GetString()) : null)
                ?? throw Refused(MediaErrorCodes.InvalidField, $"A tag of the asset {id:D} is one the service refuses. {MediaService.TagRule}"));
        }

        return MediaJson.OfTags([.. tags.Distinct(StringComparer.Ordinal)]);
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

        // A disk that drops the dots at the end of a segment reads original.png. as original.png.
        if (key.Split('/').Any(segment => segment.EndsWith('.')))
        {
            throw Refused(MediaErrorCodes.TransferFileMissing, $"The key {key} of the asset {line.Id:D} has a segment that ends with a dot, which a disk may read as another key; an import takes no such key.");
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

        // The columns of the file are those of the file the asset takes, the checked ones those the checks gave, and the others
        // the line's.
        public void ApplyTo(Asset asset, StoredFile file, AssetFields fields)
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
            asset.Duration = file.Duration;
            asset.Title = Title;
            asset.Alt = fields.Alt;
            asset.Caption = fields.Caption;
            asset.Credit = Credit;
            asset.Tags = fields.Tags;
            asset.FocalX = FocalX;
            asset.FocalY = FocalY;
            asset.IsPrivate = IsPrivate;
            asset.CreatedAt = CreatedAt;
            asset.CreatedBy = CreatedBy;
            asset.UpdatedAt = UpdatedAt;
            asset.UpdatedBy = UpdatedBy;
        }
    }

    // The file an asset takes, where it is stored and what is known of it. The checks of an upload find no duration, so a
    // file they passed has none, as after a replace through the service.
    private sealed record StoredFile(string FileName, string Provider, string Key, string MimeType, long Size, string Checksum, int? Width, int? Height, double? Duration)
    {
        public StoredFile(string provider, string key, MediaService.Upload file)
            : this(file.FileName, provider, key, file.Type.MimeType, file.Size, file.Checksum, file.Dimensions?.Width, file.Dimensions?.Height, null)
        {
        }

        public static StoredFile Of(Asset asset) =>
            new(asset.FileName, asset.StorageProvider, asset.StorageKey, asset.MimeType, asset.Size, asset.Checksum, asset.Width, asset.Height, asset.Duration);
    }

    // What the checks of a line give: the file name, and the alternative texts, captions and tags as their columns hold them.
    private sealed record AssetFields(string FileName, string Alt, string Caption, string Tags);
}
