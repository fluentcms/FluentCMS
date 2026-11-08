using System.Security.Cryptography;
using FluentCMS.Authorization;
using FluentCMS.Content;
using FluentCMS.Events;
using FluentCMS.Media;
using FluentCMS.Settings;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluentCMS.Plugins.Media;

internal sealed class MediaPermissions : IPermissionProvider
{
    public const string Read = "media.read";
    public const string Upload = "media.upload";
    public const string Update = "media.update";
    public const string Delete = "media.delete";
    public const string ManageFolders = "media.folders.manage";

    public IEnumerable<Permission> GetPermissions() =>
    [
        new Permission(Read, "List and read the assets and folders of a site. A grant limited to folders reaches those and their subfolders."),
        new Permission(Upload, "Upload assets into the folders of a site."),
        new Permission(Update, "Change the metadata of assets and move them between folders."),
        new Permission(Delete, "Delete assets."),
        new Permission(ManageFolders, "Create, rename, move and delete folders."),
    ];
}

// The media of the site of the current operation. Each call checks its permission with IPermissionChecker. A grant limited
// to folders reaches them and their subfolders (ADR 0004), and an asset or folder outside what the caller reaches is not
// found; the root is reached only by a grant with no folder limit. Uploads are read to a temporary file, where their type,
// size, checksum and image size are found, and only when every file of a request passes are they written to the storage,
// then recorded; a file written for a request that fails is deleted again.
internal sealed class MediaService(
    PluginDbContext<MediaPlugin> database,
    IPermissionChecker permissions,
    IOperationContextAccessor operations,
    ISettings<MediaSettings> settings,
    IFileStorage storage,
    IEventBus events,
    IOptions<MediaOptions> options,
    TimeProvider time,
    ILogger<MediaService> logger) : IMediaService
{
    public const int MaxPageSize = 100;
    public const int AltMaxLength = 1000;
    public const int CaptionMaxLength = 2000;
    public const int MaxTags = 50;
    public const int TagMaxLength = 64;

    public async Task<IReadOnlyList<MediaAsset>> UploadAsync(MediaUpload upload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upload);

        var site = Operation().Site;
        var reach = await ReachAsync(MediaPermissions.Upload, site, cancellationToken);
        if (upload.FolderId is { } folderId)
        {
            await FindFolderAsync(site, folderId, reach, tracked: false, cancellationToken);
        }
        else if (!reach.IsAll)
        {
            throw new PermissionDeniedException(MediaPermissions.Upload, site);
        }

        var rules = await settings.GetAsync(site, cancellationToken);
        var files = upload.Files ?? [];
        if (files.Count == 0 || files.Count > rules.MaxFilesPerRequest)
        {
            throw Invalid("files", $"An upload has from 1 to {rules.MaxFilesPerRequest} files.");
        }

        var requestLimit = Math.Min(rules.MaxRequestSize, options.Value.MaxRequestBodySize);
        List<Upload> prepared = [];
        List<string> written = [];
        try
        {
            long total = 0;
            for (var index = 0; index < files.Count; index++)
            {
                var file = await PrepareAsync(files[index], index, rules, requestLimit - total, cancellationToken);
                prepared.Add(file);
                total += file.Size;
            }

            var actor = Operation().AuditPrincipal.Id;
            var now = time.GetUtcNow();
            List<Asset> assets = [];
            foreach (var file in prepared)
            {
                var id = Guid.CreateVersion7();
                var key = $"{site.Value:N}/{id:N}/original{file.Type.Extension}";
                file.Content.Position = 0;
                await storage.WriteAsync(key, file.Content, cancellationToken);
                written.Add(key);
                assets.Add(new Asset
                {
                    Id = id,
                    SiteId = site.Value,
                    FolderId = upload.FolderId,
                    FileName = file.FileName,
                    StorageProvider = storage.Name,
                    StorageKey = key,
                    MimeType = file.Type.MimeType,
                    Size = file.Size,
                    Checksum = file.Checksum,
                    Width = file.Dimensions?.Width,
                    Height = file.Dimensions?.Height,
                    CreatedAt = now,
                    CreatedBy = actor,
                    UpdatedAt = now,
                    UpdatedBy = actor,
                });
            }

            database.AddRange(assets);
            await database.SaveChangesAsync(cancellationToken);
            written.Clear();

            foreach (var asset in assets)
            {
                await events.PublishAsync(new MediaAssetUploadedEvent(asset.Id, asset.FolderId, asset.FileName, asset.MimeType, asset.Size), cancellationToken);
            }

            var locale = Operation().Locale;
            return [.. assets.Select(asset => Info(asset, locale))];
        }
        finally
        {
            foreach (var file in prepared)
            {
                await file.Content.DisposeAsync();
            }

            foreach (var key in written)
            {
                await ForgetAsync(key);
            }
        }
    }

    public async Task<MediaAsset> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var site = Operation().Site;
        var reach = await ReachAsync(MediaPermissions.Read, site, cancellationToken);
        return Info(await FindAssetAsync(site, id, reach, tracked: false, cancellationToken), Operation().Locale);
    }

    public async Task<PagedResult<MediaAsset>> ListAsync(MediaQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.PageSize < 1 || query.PageSize > MaxPageSize)
        {
            throw Invalid("pageSize", $"The page size is from 1 to {MaxPageSize}.");
        }

        if (query.Page < 1 || (long)(query.Page - 1) * query.PageSize > int.MaxValue)
        {
            throw Invalid("page", "The page number starts at 1.");
        }

        if (query.RootOnly && query.FolderId is not null)
        {
            throw Invalid("folder", "A list is of one folder or of the root, not both.");
        }

        var site = Operation().Site;
        var reach = await ReachAsync(MediaPermissions.Read, site, cancellationToken);
        var assets = database.Set<Asset>().AsNoTracking().Where(asset => asset.SiteId == site.Value && asset.DeletedAt == null);

        if (query.FolderId is { } folderId)
        {
            var folder = await FindFolderAsync(site, folderId, reach, tracked: false, cancellationToken);
            if (query.IncludeSubfolders)
            {
                var subtree = (await SubtreeAsync(site, folder.Path, cancellationToken)).Where(item => reach.Includes(item)).ToList();
                assets = assets.Where(asset => asset.FolderId != null && subtree.Contains(asset.FolderId.Value));
            }
            else
            {
                assets = assets.Where(asset => asset.FolderId == folderId);
            }
        }
        else if (query.RootOnly)
        {
            assets = reach.IsAll ? assets.Where(asset => asset.FolderId == null) : assets.Where(_ => false);
        }
        else if (!reach.IsAll)
        {
            var reached = reach.Folders.ToList();
            assets = assets.Where(asset => asset.FolderId != null && reached.Contains(asset.FolderId.Value));
        }

        if (query.MimeType is { } mimeType)
        {
            if (mimeType.Length is 0 or > MediaModel.MimeTypeMaxLength || mimeType.Count(character => character == '/') != 1)
            {
                throw Invalid("type", "A type is a MIME type such as image/png, or a family such as image/*.");
            }

            var family = mimeType.EndsWith("/*", StringComparison.Ordinal) ? mimeType[..^1] : null;
            assets = family is not null ? assets.Where(asset => asset.MimeType.StartsWith(family)) : assets.Where(asset => asset.MimeType == mimeType);
        }

        if (query.Tag is { } tag)
        {
            var needle = MediaJson.TagNeedle(ValidTag(tag, "tag"));
            assets = assets.Where(asset => asset.Tags.Contains(needle));
        }

        var total = await assets.LongCountAsync(cancellationToken);
        var rows = await assets.OrderByDescending(asset => asset.CreatedAt).ThenByDescending(asset => asset.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .ToListAsync(cancellationToken);
        var locale = Operation().Locale;
        return new PagedResult<MediaAsset>([.. rows.Select(asset => Info(asset, locale))], total, query.Page, query.PageSize, null);
    }

    public async Task<MediaAsset> UpdateAsync(Guid id, MediaAssetChanges changes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);

        var operation = Operation();
        var site = operation.Site;
        var reach = await ReachAsync(MediaPermissions.Update, site, cancellationToken);
        var asset = await FindAssetAsync(site, id, reach, tracked: true, cancellationToken);
        if (changes == new MediaAssetChanges())
        {
            return Info(asset, operation.Locale);
        }

        List<ValidationError> errors = [];
        Check(errors, "title", changes.Title, MediaModel.TitleMaxLength);
        Check(errors, "alt", changes.Alt, AltMaxLength);
        Check(errors, "caption", changes.Caption, CaptionMaxLength);
        Check(errors, "credit", changes.Credit, MediaModel.CreditMaxLength);
        List<string>? tags = null;
        if (changes.Tags is { } given)
        {
            if (given.Count > MaxTags)
            {
                errors.Add(new ValidationError("tags", MediaErrorCodes.InvalidField, $"An asset has at most {MaxTags} tags."));
            }

            tags = [];
            for (var index = 0; index < given.Count; index++)
            {
                try
                {
                    tags.Add(ValidTag(given[index], $"tags[{index}]"));
                }
                catch (ValidationException exception)
                {
                    errors.AddRange(exception.Errors);
                }
            }
        }

        if (changes.FocalPoint is { } focal && !(focal.X is >= 0 and <= 1 && focal.Y is >= 0 and <= 1))
        {
            errors.Add(new ValidationError("focalPoint", MediaErrorCodes.InvalidField, "A focal point has x and y from 0 to 1."));
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        if (changes.Title is { } title)
        {
            asset.Title = Clear(title);
        }

        if (changes.Credit is { } credit)
        {
            asset.Credit = Clear(credit);
        }

        if (changes.Alt is { } alt)
        {
            asset.Alt = MediaJson.Set(asset.Alt, operation.Locale, Clear(alt));
        }

        if (changes.Caption is { } caption)
        {
            asset.Caption = MediaJson.Set(asset.Caption, operation.Locale, Clear(caption));
        }

        if (tags is not null)
        {
            asset.Tags = MediaJson.OfTags([.. tags.Distinct(StringComparer.Ordinal)]);
        }

        if (changes.FocalPoint is { } point)
        {
            (asset.FocalX, asset.FocalY) = (point.X, point.Y);
        }

        if (changes.IsPrivate is { } isPrivate)
        {
            asset.IsPrivate = isPrivate;
        }

        asset.UpdatedAt = time.GetUtcNow();
        asset.UpdatedBy = operation.AuditPrincipal.Id;
        await database.SaveChangesAsync(cancellationToken);
        await events.PublishAsync(new MediaAssetUpdatedEvent(asset.Id, asset.FolderId, asset.FileName), cancellationToken);
        return Info(asset, operation.Locale);
    }

    public async Task<MediaAsset> MoveAsync(Guid id, Guid? folderId, CancellationToken cancellationToken = default)
    {
        var operation = Operation();
        var site = operation.Site;
        var reach = await ReachAsync(MediaPermissions.Update, site, cancellationToken);
        var asset = await FindAssetAsync(site, id, reach, tracked: true, cancellationToken);
        if (folderId is { } target)
        {
            await FindFolderAsync(site, target, reach, tracked: false, cancellationToken);
        }
        else if (!reach.IsAll)
        {
            throw new PermissionDeniedException(MediaPermissions.Update, site);
        }

        var previous = asset.FolderId;
        if (previous == folderId)
        {
            return Info(asset, operation.Locale);
        }

        asset.FolderId = folderId;
        asset.UpdatedAt = time.GetUtcNow();
        asset.UpdatedBy = operation.AuditPrincipal.Id;
        await database.SaveChangesAsync(cancellationToken);
        await events.PublishAsync(new MediaAssetMovedEvent(asset.Id, asset.FolderId, asset.FileName, previous), cancellationToken);
        return Info(asset, operation.Locale);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var operation = Operation();
        var site = operation.Site;
        var reach = await ReachAsync(MediaPermissions.Delete, site, cancellationToken);
        var asset = await FindAssetAsync(site, id, reach, tracked: true, cancellationToken);

        var now = time.GetUtcNow();
        asset.DeletedAt = now;
        asset.UpdatedAt = now;
        asset.UpdatedBy = operation.AuditPrincipal.Id;
        await database.SaveChangesAsync(cancellationToken);
        await events.PublishAsync(new MediaAssetDeletedEvent(asset.Id, asset.FolderId, asset.FileName), cancellationToken);
    }

    public async Task<IReadOnlyList<MediaFolder>> ListFoldersAsync(CancellationToken cancellationToken = default)
    {
        var site = Operation().Site;
        var reach = await ReachAsync(MediaPermissions.Read, site, cancellationToken);
        var folders = await database.Set<Folder>().AsNoTracking().Where(folder => folder.SiteId == site.Value).ToListAsync(cancellationToken);
        return [.. folders.Where(folder => reach.Includes(folder.Id)).OrderBy(folder => folder.Name, StringComparer.Ordinal).ThenBy(folder => folder.Id).Select(Info)];
    }

    public async Task<MediaFolder> CreateFolderAsync(string name, Guid? parentId, CancellationToken cancellationToken = default)
    {
        var site = Operation().Site;
        var reach = await ReachAsync(MediaPermissions.ManageFolders, site, cancellationToken);
        var parent = parentId is { } id ? await FindFolderAsync(site, id, reach, tracked: false, cancellationToken) : null;
        if (parent is null && !reach.IsAll)
        {
            throw new PermissionDeniedException(MediaPermissions.ManageFolders, site);
        }

        var valid = ValidName(name);
        if (Depth(parent?.Path ?? "/") + 1 > MediaModel.MaxDepth)
        {
            throw Invalid("parentId", $"Folders nest at most {MediaModel.MaxDepth} deep.");
        }

        var folder = new Folder { Id = Guid.CreateVersion7(), SiteId = site.Value, ParentId = parentId, Name = valid };
        folder.Path = $"{parent?.Path ?? "/"}{folder.Id:D}/";
        database.Add(folder);
        await SaveFolderAsync(folder, cancellationToken);
        return Info(folder);
    }

    public async Task<MediaFolder> RenameFolderAsync(Guid id, string name, CancellationToken cancellationToken = default)
    {
        var site = Operation().Site;
        var reach = await ReachAsync(MediaPermissions.ManageFolders, site, cancellationToken);
        var folder = await FindFolderAsync(site, id, reach, tracked: true, cancellationToken);
        folder.Name = ValidName(name);
        await SaveFolderAsync(folder, cancellationToken);
        return Info(folder);
    }

    // The folder takes its subfolders along, each path rewritten under the new parent; assets stay in their folders.
    public async Task<MediaFolder> MoveFolderAsync(Guid id, Guid? parentId, CancellationToken cancellationToken = default)
    {
        var site = Operation().Site;
        var reach = await ReachAsync(MediaPermissions.ManageFolders, site, cancellationToken);
        var folder = await FindFolderAsync(site, id, reach, tracked: true, cancellationToken);
        var parent = parentId is { } target ? await FindFolderAsync(site, target, reach, tracked: false, cancellationToken) : null;
        if (parent is null && !reach.IsAll)
        {
            throw new PermissionDeniedException(MediaPermissions.ManageFolders, site);
        }

        if (parent is not null && parent.Path.StartsWith(folder.Path, StringComparison.Ordinal))
        {
            throw new PluginException(MediaErrorCodes.FolderCycle, $"The folder {id:D} cannot move into itself or one of its subfolders.");
        }

        if (folder.ParentId == parentId)
        {
            return Info(folder);
        }

        var oldPrefix = folder.Path;
        var newPrefix = $"{parent?.Path ?? "/"}{folder.Id:D}/";
        var subtree = await database.Set<Folder>().Where(item => item.SiteId == site.Value && item.Path.StartsWith(oldPrefix)).ToListAsync(cancellationToken);
        if (subtree.Max(item => Depth(item.Path)) - Depth(oldPrefix) + Depth(newPrefix) > MediaModel.MaxDepth)
        {
            throw Invalid("parentId", $"Folders nest at most {MediaModel.MaxDepth} deep.");
        }

        foreach (var item in subtree)
        {
            item.Path = newPrefix + item.Path[oldPrefix.Length..];
        }

        folder.ParentId = parentId;
        await SaveFolderAsync(folder, cancellationToken);
        return Info(folder);
    }

    public async Task DeleteFolderAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var site = Operation().Site;
        var reach = await ReachAsync(MediaPermissions.ManageFolders, site, cancellationToken);
        var folder = await FindFolderAsync(site, id, reach, tracked: true, cancellationToken);
        if (await database.Set<Folder>().AnyAsync(item => item.SiteId == site.Value && item.ParentId == id, cancellationToken)
            || await database.Set<Asset>().AnyAsync(asset => asset.SiteId == site.Value && asset.FolderId == id && asset.DeletedAt == null, cancellationToken))
        {
            throw new PluginException(MediaErrorCodes.FolderNotEmpty, $"The folder {id:D} holds folders or assets; move or delete them first.");
        }

        database.Remove(folder);
        await database.SaveChangesAsync(cancellationToken);
    }

    // Every folder and the root when a grant has no folder limit, otherwise the folders the grants name and their subfolders.
    // No grant at all is a refusal.
    private async Task<FolderReach> ReachAsync(string permission, SiteId site, CancellationToken cancellationToken)
    {
        var resources = await permissions.ResourcesAsync(permission, site, cancellationToken);
        if (resources is null)
        {
            return FolderReach.All;
        }

        if (resources.Count == 0)
        {
            throw new PermissionDeniedException(permission, site);
        }

        var named = resources.Select(resource => Guid.TryParse(resource, out var id) ? id : (Guid?)null).OfType<Guid>().ToList();
        var paths = await database.Set<Folder>().AsNoTracking()
            .Where(folder => folder.SiteId == site.Value && named.Contains(folder.Id))
            .Select(folder => folder.Path)
            .ToListAsync(cancellationToken);

        HashSet<Guid> folders = [];
        foreach (var path in paths)
        {
            folders.UnionWith(await SubtreeAsync(site, path, cancellationToken));
        }

        return new FolderReach(folders);
    }

    private Task<List<Guid>> SubtreeAsync(SiteId site, string path, CancellationToken cancellationToken) =>
        database.Set<Folder>().AsNoTracking().Where(folder => folder.SiteId == site.Value && folder.Path.StartsWith(path)).Select(folder => folder.Id).ToListAsync(cancellationToken);

    private async Task<Asset> FindAssetAsync(SiteId site, Guid id, FolderReach reach, bool tracked, CancellationToken cancellationToken)
    {
        var assets = tracked ? database.Set<Asset>() : database.Set<Asset>().AsNoTracking();
        var asset = await assets.FirstOrDefaultAsync(asset => asset.Id == id && asset.SiteId == site.Value && asset.DeletedAt == null, cancellationToken);
        return asset is not null && reach.Includes(asset.FolderId)
            ? asset
            : throw new PluginException(MediaErrorCodes.AssetNotFound, $"The site has no media asset {id:D}.");
    }

    private async Task<Folder> FindFolderAsync(SiteId site, Guid id, FolderReach reach, bool tracked, CancellationToken cancellationToken)
    {
        var folders = tracked ? database.Set<Folder>() : database.Set<Folder>().AsNoTracking();
        var folder = await folders.FirstOrDefaultAsync(folder => folder.Id == id && folder.SiteId == site.Value, cancellationToken);
        return folder is not null && reach.Includes(folder.Id)
            ? folder
            : throw new PluginException(MediaErrorCodes.FolderNotFound, $"The site has no media folder {id:D}.");
    }

    // Two writers that both found the name free meet at the unique index, except at the root, whose null parent the index
    // does not compare.
    private async Task SaveFolderAsync(Folder folder, CancellationToken cancellationToken)
    {
        if (await NameTakenAsync(folder, cancellationToken))
        {
            throw NameTaken(folder.Name);
        }

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (await NameTakenAsync(folder, CancellationToken.None))
            {
                throw NameTaken(folder.Name);
            }

            throw;
        }
    }

    private Task<bool> NameTakenAsync(Folder folder, CancellationToken cancellationToken) =>
        database.Set<Folder>().AsNoTracking().AnyAsync(item => item.SiteId == folder.SiteId && item.ParentId == folder.ParentId && item.Name == folder.Name && item.Id != folder.Id, cancellationToken);

    private static PluginException NameTaken(string name) => new(MediaErrorCodes.FolderNameTaken, $"The folder already holds a folder named '{name}'.");

    // Read once to a temporary file, counting and hashing on the way, so that a file over its limit is refused without
    // reading it to the end, and its type and image size are read from the bytes as stored.
    private async Task<Upload> PrepareAsync(MediaUploadFile file, int index, MediaSettings rules, long requestLeft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

        var name = FileNameOf(file.FileName) ?? throw Invalid($"files[{index}].fileName", $"A file needs a name of at most {MediaModel.FileNameMaxLength} characters, without control characters.");
        Stream content = new FileStream(Path.Combine(Path.GetTempPath(), $"fluentcms-media-{Guid.NewGuid():N}.tmp"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long size = 0;
            int read;
            while ((read = await file.Content.ReadAsync(buffer, cancellationToken)) > 0)
            {
                size += read;
                if (size > rules.MaxFileSize)
                {
                    throw new PluginException(MediaErrorCodes.TooLarge, $"The file '{name}' is over the limit of {rules.MaxFileSize} bytes a file.");
                }

                if (size > requestLeft)
                {
                    throw new PluginException(MediaErrorCodes.TooLarge, $"The files of the upload are over the limit of {Math.Min(rules.MaxRequestSize, options.Value.MaxRequestBodySize)} bytes a request.");
                }

                hash.AppendData(buffer, 0, read);
                await content.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            content.Position = 0;
            var header = new byte[MediaTypes.HeaderLength];
            var length = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
            var bytes = header.AsSpan(0, length);
            if (MediaTypes.IsExecutable(bytes) || MediaTypes.HasExecutableName(name))
            {
                throw new PluginException(MediaErrorCodes.Executable, $"The file '{name}' is an executable, which is never stored.");
            }

            var checksum = Convert.ToHexStringLower(hash.GetHashAndReset());
            var type = MediaTypes.Detect(bytes);
            if (type is null && MediaTypes.MayBeSvg(bytes))
            {
                content.Position = 0;
                if (SvgDocument.Parse(content) is { } svg)
                {
                    if (rules.Svg != SvgHandling.Sanitize)
                    {
                        throw new PluginException(MediaErrorCodes.SvgRefused, $"The file '{name}' is an SVG, which the media settings of the site refuse.");
                    }

                    SvgDocument.Sanitize(svg);
                    var sanitized = SvgDocument.Save(svg);
                    await content.DisposeAsync();
                    content = new MemoryStream(sanitized, writable: false);
                    return new Upload(name, MediaTypes.Svg, content, sanitized.Length, Convert.ToHexStringLower(SHA256.HashData(sanitized)), null);
                }
            }

            var allowed = rules.AllowedTypes ?? [];
            if (type is null || !allowed.Contains(type.MimeType, StringComparer.Ordinal))
            {
                throw new PluginException(MediaErrorCodes.TypeNotAllowed, $"The file '{name}' is not of a type the site allows: {string.Join(", ", allowed)}{(rules.Svg == SvgHandling.Sanitize ? ", " + MediaTypes.Svg.MimeType : "")}.");
            }

            var dimensions = type.IsImage ? await ImageSize.ReadAsync(content, type, cancellationToken) : null;
            return new Upload(name, type, content, size, checksum, dimensions);
        }
        catch
        {
            await content.DisposeAsync();
            throw;
        }
    }

    private async Task ForgetAsync(string key)
    {
        try
        {
            await storage.DeleteAsync(key, CancellationToken.None);
        }
        catch (Exception exception)
        {
            MediaLog.NotDeleted(logger, storage.Name, key, exception);
        }
    }

    // The name without a directory, from either kind of separator, since browsers on Windows may send the whole path.
    private static string? FileNameOf(string? given)
    {
        var name = given?[(given.LastIndexOfAny(['/', '\\']) + 1)..].Trim();
        return string.IsNullOrEmpty(name) || name.Length > MediaModel.FileNameMaxLength || name is "." or ".." || name.Any(char.IsControl) ? null : name;
    }

    private static string ValidName(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > MediaModel.FolderNameMaxLength || trimmed.Any(character => char.IsControl(character) || character is '/' or '\\')
            ? throw Invalid("name", $"A folder needs a name of at most {MediaModel.FolderNameMaxLength} characters, without slashes or control characters.")
            : trimmed;
    }

    private static string ValidTag(string? tag, string path)
    {
        var trimmed = tag?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > TagMaxLength || trimmed.Any(character => char.IsControl(character) || character is '"' or '\\')
            ? throw Invalid(path, $"A tag has from 1 to {TagMaxLength} characters, without quotes, backslashes or control characters.")
            : trimmed;
    }

    private static void Check(List<ValidationError> errors, string path, string? value, int maxLength)
    {
        if (value is not null && value.Trim().Length > maxLength)
        {
            errors.Add(new ValidationError(path, MediaErrorCodes.InvalidField, $"The {path} has at most {maxLength} characters."));
        }
    }

    private static string? Clear(string value) => value.Trim() is { Length: > 0 } trimmed ? trimmed : null;

    private static int Depth(string path) => path.Count(character => character == '/') - 1;

    public static ValidationException Invalid(string path, string message) => new([new ValidationError(path, MediaErrorCodes.InvalidField, message)]);

    private OperationContext Operation() =>
        operations.Current ?? throw new InvalidOperationException("The media of a site is managed inside an operation, which names the site.");

    private static MediaAsset Info(Asset asset, string locale) => new()
    {
        Id = asset.Id,
        SiteId = asset.SiteId,
        FolderId = asset.FolderId,
        FileName = asset.FileName,
        MimeType = asset.MimeType,
        Size = asset.Size,
        Checksum = asset.Checksum,
        Width = asset.Width,
        Height = asset.Height,
        Title = asset.Title,
        Locale = locale,
        Alt = MediaJson.Map(asset.Alt).GetValueOrDefault(locale),
        Caption = MediaJson.Map(asset.Caption).GetValueOrDefault(locale),
        Credit = asset.Credit,
        Tags = MediaJson.Tags(asset.Tags),
        FocalPoint = asset is { FocalX: { } x, FocalY: { } y } ? new MediaFocalPoint(x, y) : null,
        IsPrivate = asset.IsPrivate,
        CreatedAt = asset.CreatedAt,
        CreatedBy = asset.CreatedBy,
        UpdatedAt = asset.UpdatedAt,
        UpdatedBy = asset.UpdatedBy,
    };

    private static MediaFolder Info(Folder folder) => new(folder.Id, folder.SiteId, folder.ParentId, folder.Name);

    private sealed record Upload(string FileName, MediaType Type, Stream Content, long Size, string Checksum, (int Width, int Height)? Dimensions);
}

// The folders a permission reaches: all of them and the root, or a set of folders.
internal sealed class FolderReach(IReadOnlySet<Guid>? folders)
{
    public static readonly FolderReach All = new(null);

    public bool IsAll => folders is null;

    public IReadOnlySet<Guid> Folders => folders ?? throw new InvalidOperationException("Every folder is reached.");

    public bool Includes(Guid? folder) => folders is null || (folder is { } id && folders.Contains(id));
}
