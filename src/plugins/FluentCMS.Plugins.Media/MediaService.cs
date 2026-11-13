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
    public const string ManagePresets = "media.presets.manage";

    public IEnumerable<Permission> GetPermissions() =>
    [
        new Permission(Read, "List and read the assets and folders of a site. A grant limited to folders reaches those and their subfolders."),
        new Permission(Upload, "Upload assets into the folders of a site."),
        new Permission(Update, "Change the metadata of assets, replace their files and move them between folders."),
        new Permission(Delete, "Delete assets."),
        new Permission(ManageFolders, "Create, rename, move and delete folders."),
        new Permission(ManagePresets, "Create, change and delete the image presets of a site. A grant limited to folders gives none of it."),
    ];
}

// The media of the site of the current operation, which is the library of the default site when the sites share one
// (MediaLibraries). Each call checks its permission with IPermissionChecker in the site of the operation. A grant limited
// to folders reaches them and their subfolders (ADR 0004), and an asset or folder outside what the caller reaches is not
// found; the root is reached only by a grant with no folder limit, and the presets of the site only by such a grant of
// media.presets.manage. Uploads are read to a temporary file, where their type, size, checksum and image size are found,
// and only when every file of a request passes are they written to the storage, then recorded; a file written for a
// request that fails is deleted again. A replace writes its file under a key of its own, so that the old file serves until
// the row names the new one; the old file and the variants are deleted after, from the storage the asset recorded. New files
// go to the storage in the slot.
internal sealed class MediaService(
    PluginDbContext<MediaPlugin> database,
    IPermissionChecker permissions,
    IOperationContextAccessor operations,
    MediaLibraries libraries,
    ISettings<MediaSettings> settings,
    MediaStorage storages,
    IEventBus events,
    IOptions<MediaOptions> options,
    IImageProcessor processor,
    MediaSigning signing,
    IContentReferences references,
    MediaCache cache,
    MediaTransforms transforms,
    TimeProvider time,
    ILogger<MediaService> logger) : IMediaService
{
    // The target type of an asset in cms_reference.
    public const string ReferenceTarget = "media";

    public const int MaxPageSize = 100;
    public const int MaxSearchLength = 256;
    public const int AltMaxLength = 1000;
    public const int CaptionMaxLength = 2000;
    public const int MaxTags = 50;
    public const int TagMaxLength = 64;

    public async Task<IReadOnlyList<MediaAsset>> UploadAsync(MediaUpload upload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upload);

        var (site, library) = await ScopeAsync(Operation(), cancellationToken);
        var reach = await ReachAsync(MediaPermissions.Upload, site, library, cancellationToken);
        if (upload.FolderId is { } folderId)
        {
            await FindFolderAsync(library, folderId, reach, tracked: false, cancellationToken);
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
                var key = $"{library.Value:N}/{id:N}/original{file.Type.Extension}";
                file.Content.Position = 0;
                await storages.Current.WriteAsync(key, file.Content, cancellationToken);
                written.Add(key);
                assets.Add(new Asset
                {
                    Id = id,
                    SiteId = library.Value,
                    FolderId = upload.FolderId,
                    FileName = file.FileName,
                    StorageProvider = storages.Current.Name,
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
        var (site, library) = await ScopeAsync(Operation(), cancellationToken);
        var reach = await ReachAsync(MediaPermissions.Read, site, library, cancellationToken);
        return Info(await FindAssetAsync(library, id, reach, tracked: false, cancellationToken), Operation().Locale);
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

        var (site, library) = await ScopeAsync(Operation(), cancellationToken);
        var reach = await ReachAsync(MediaPermissions.Read, site, library, cancellationToken);
        var assets = database.Set<Asset>().AsNoTracking().Where(asset => asset.SiteId == library.Value && asset.DeletedAt == null);

        if (query.FolderId is { } folderId)
        {
            var folder = await FindFolderAsync(library, folderId, reach, tracked: false, cancellationToken);
            if (query.IncludeSubfolders)
            {
                var subtree = (await SubtreeAsync(library, folder.Path, cancellationToken)).Where(item => reach.Includes(item)).ToList();
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

        if (query.Search is { } search)
        {
            if (string.IsNullOrWhiteSpace(search) || search.Length > MaxSearchLength)
            {
                throw Invalid("search", $"A search is a text of 1 to {MaxSearchLength} characters.");
            }

            // The tags are a JSON array in one column, so a text with a character of its syntax is not looked for there.
            var needle = search.Trim().ToLowerInvariant();
            var inTags = needle.IndexOfAny(['"', '\\', '[', ']', ',']) < 0;
#pragma warning disable CA1304, CA1311, CA1862 // The database lowers the columns, where lower() takes no culture and a comparison none either.
            assets = assets.Where(asset =>
                (asset.Title != null && asset.Title.ToLower().Contains(needle))
                || asset.FileName.ToLower().Contains(needle)
                || (inTags && asset.Tags.ToLower().Contains(needle)));
#pragma warning restore CA1304, CA1311, CA1862
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
        var (site, library) = await ScopeAsync(operation, cancellationToken);
        var reach = await ReachAsync(MediaPermissions.Update, site, library, cancellationToken);
        var asset = await FindAssetAsync(library, id, reach, tracked: true, cancellationToken);
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
        var (site, library) = await ScopeAsync(operation, cancellationToken);
        var reach = await ReachAsync(MediaPermissions.Update, site, library, cancellationToken);
        var asset = await FindAssetAsync(library, id, reach, tracked: true, cancellationToken);
        if (folderId is { } target)
        {
            await FindFolderAsync(library, target, reach, tracked: false, cancellationToken);
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

    // The references go before the asset, so that a delete that fails between the two is tried again rather than leaving
    // references to an asset that is gone; whether an entry refers to it is asked across sites, as an id is unique.
    // A request that read the old row of a replaced asset just before the save may find its file gone and answer 404.
    public async Task DeleteAsync(Guid id, bool force, CancellationToken cancellationToken = default)
    {
        var operation = Operation();
        var (site, library) = await ScopeAsync(operation, cancellationToken);
        var reach = await ReachAsync(MediaPermissions.Delete, site, library, cancellationToken);
        var asset = await FindAssetAsync(library, id, reach, tracked: true, cancellationToken);

        if (force)
        {
            await references.RemoveAsync(ReferenceTarget, id, cancellationToken);
        }
        else if (await references.CountAsync(ReferenceTarget, id, cancellationToken) > 0)
        {
            // No count: it takes in entries the caller may not read.
            throw new PluginException(MediaErrorCodes.AssetInUse, $"Entries refer to the asset {id:D}; take it out of them, or delete it with force=true, which removes it from them.");
        }

        var now = time.GetUtcNow();
        asset.DeletedAt = now;
        asset.UpdatedAt = now;
        asset.UpdatedBy = operation.AuditPrincipal.Id;
        await database.SaveChangesAsync(cancellationToken);
        await events.PublishAsync(new MediaAssetDeletedEvent(asset.Id, asset.FolderId, asset.FileName), cancellationToken);
    }

    public async Task<MediaAsset> ReplaceAsync(Guid id, MediaUploadFile file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        var operation = Operation();
        var (site, library) = await ScopeAsync(operation, cancellationToken);
        var reach = await ReachAsync(MediaPermissions.Update, site, library, cancellationToken);
        var asset = await FindAssetAsync(library, id, reach, tracked: true, cancellationToken);
        var rules = await settings.GetAsync(site, cancellationToken);
        var prepared = await PrepareAsync(file, 0, rules, Math.Min(rules.MaxRequestSize, options.Value.MaxRequestBodySize), cancellationToken);
        string? written = null;
        try
        {
            var key = $"{library.Value:N}/{id:N}/original-{Guid.CreateVersion7():N}{prepared.Type.Extension}";
            prepared.Content.Position = 0;
            await storages.Current.WriteAsync(key, prepared.Content, cancellationToken);
            written = key;

            var (oldStorage, oldKey) = (asset.StorageProvider, asset.StorageKey);
            var variants = await database.Set<Variant>().Where(variant => variant.AssetId == id).ToListAsync(cancellationToken);
            database.RemoveRange(variants);
            asset.FileName = prepared.FileName;
            asset.StorageProvider = storages.Current.Name;
            asset.StorageKey = key;
            asset.MimeType = prepared.Type.MimeType;
            asset.Size = prepared.Size;
            asset.Checksum = prepared.Checksum;
            asset.Width = prepared.Dimensions?.Width;
            asset.Height = prepared.Dimensions?.Height;
            asset.Duration = null;
            asset.UpdatedAt = time.GetUtcNow();
            asset.UpdatedBy = operation.AuditPrincipal.Id;
            await database.SaveChangesAsync(cancellationToken);
            written = null;

            foreach (var stale in variants.Select(variant => variant.StorageKey).Prepend(oldKey))
            {
                await ForgetReplacedAsync(id, oldStorage, stale);
            }

            await events.PublishAsync(new MediaAssetReplacedEvent(asset.Id, asset.FolderId, asset.FileName, asset.MimeType, asset.Size), cancellationToken);
            return Info(asset, operation.Locale);
        }
        finally
        {
            await prepared.Content.DisposeAsync();
            if (written is not null)
            {
                await ForgetAsync(written);
            }
        }
    }

    public async Task<IReadOnlyList<ContentReference>> GetUsageAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var (site, library) = await ScopeAsync(Operation(), cancellationToken);
        var reach = await ReachAsync(MediaPermissions.Read, site, library, cancellationToken);
        await FindAssetAsync(library, id, reach, tracked: false, cancellationToken);
        return await references.FindAsync(ReferenceTarget, id, cancellationToken);
    }

    public async Task<MediaDownloadUrl> GetDownloadUrlAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var (site, library) = await ScopeAsync(Operation(), cancellationToken);
        var reach = await ReachAsync(MediaPermissions.Read, site, library, cancellationToken);
        var asset = await FindAssetAsync(library, id, reach, tracked: false, cancellationToken);
        var rules = await settings.GetAsync(site, cancellationToken);
        var expires = time.GetUtcNow().ToUnixTimeSeconds() + rules.DownloadUrlSeconds;
        return new MediaDownloadUrl(await MediaOriginals.UrlAsync(signing, id, asset.FileName, expires, cancellationToken), DateTimeOffset.FromUnixTimeSeconds(expires));
    }

    // A private asset is its folder's, so the reach of media.read decides, as for the management routes; one out of reach
    // is not found, which says no more than an id that is no asset.
    public async Task<MediaDelivery> DeliverAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var operation = Operation();
        var (site, library) = await ScopeAsync(operation, cancellationToken);
        var asset = await database.Set<Asset>().AsNoTracking().FirstOrDefaultAsync(item => item.Id == id && item.SiteId == library.Value && item.DeletedAt == null, cancellationToken)
            ?? throw AssetNotFound(id);
        if (!asset.IsPrivate)
        {
            return new MediaDelivery(Info(asset, operation.Locale), await UrlsAsync(asset, cancellationToken));
        }

        FolderReach reach;
        try
        {
            reach = await ReachAsync(MediaPermissions.Read, site, library, cancellationToken);
        }
        catch (PermissionDeniedException)
        {
            throw AssetNotFound(id);
        }

        return reach.Includes(asset.FolderId) ? new MediaDelivery(Info(asset, operation.Locale), null) : throw AssetNotFound(id);
    }

    public async Task<IReadOnlyList<MediaFolder>> ListFoldersAsync(CancellationToken cancellationToken = default)
    {
        var (site, library) = await ScopeAsync(Operation(), cancellationToken);
        var reach = await ReachAsync(MediaPermissions.Read, site, library, cancellationToken);
        var folders = await database.Set<Folder>().AsNoTracking().Where(folder => folder.SiteId == library.Value).ToListAsync(cancellationToken);
        return [.. folders.Where(folder => reach.Includes(folder.Id)).OrderBy(folder => folder.Name, StringComparer.Ordinal).ThenBy(folder => folder.Id).Select(Info)];
    }

    public async Task<MediaFolder> CreateFolderAsync(string name, Guid? parentId, CancellationToken cancellationToken = default)
    {
        var (site, library) = await ScopeAsync(Operation(), cancellationToken);
        var reach = await ReachAsync(MediaPermissions.ManageFolders, site, library, cancellationToken);
        var parent = parentId is { } id ? await FindFolderAsync(library, id, reach, tracked: false, cancellationToken) : null;
        if (parent is null && !reach.IsAll)
        {
            throw new PermissionDeniedException(MediaPermissions.ManageFolders, site);
        }

        var valid = ValidName(name);
        if (Depth(parent?.Path ?? "/") + 1 > MediaModel.MaxDepth)
        {
            throw Invalid("parentId", $"Folders nest at most {MediaModel.MaxDepth} deep.");
        }

        var folder = new Folder { Id = Guid.CreateVersion7(), SiteId = library.Value, ParentId = parentId, Name = valid };
        folder.Path = $"{parent?.Path ?? "/"}{folder.Id:D}/";
        database.Add(folder);
        await SaveFolderAsync(folder, cancellationToken);
        return Info(folder);
    }

    public async Task<MediaFolder> RenameFolderAsync(Guid id, string name, CancellationToken cancellationToken = default)
    {
        var (site, library) = await ScopeAsync(Operation(), cancellationToken);
        var reach = await ReachAsync(MediaPermissions.ManageFolders, site, library, cancellationToken);
        var folder = await FindFolderAsync(library, id, reach, tracked: true, cancellationToken);
        folder.Name = ValidName(name);
        await SaveFolderAsync(folder, cancellationToken);
        return Info(folder);
    }

    // The folder takes its subfolders along, each path rewritten under the new parent; assets stay in their folders.
    public async Task<MediaFolder> MoveFolderAsync(Guid id, Guid? parentId, CancellationToken cancellationToken = default)
    {
        var (site, library) = await ScopeAsync(Operation(), cancellationToken);
        var reach = await ReachAsync(MediaPermissions.ManageFolders, site, library, cancellationToken);
        var folder = await FindFolderAsync(library, id, reach, tracked: true, cancellationToken);
        var parent = parentId is { } target ? await FindFolderAsync(library, target, reach, tracked: false, cancellationToken) : null;
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
        var subtree = await database.Set<Folder>().Where(item => item.SiteId == library.Value && item.Path.StartsWith(oldPrefix)).ToListAsync(cancellationToken);
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
        var (site, library) = await ScopeAsync(Operation(), cancellationToken);
        var reach = await ReachAsync(MediaPermissions.ManageFolders, site, library, cancellationToken);
        var folder = await FindFolderAsync(library, id, reach, tracked: true, cancellationToken);
        if (await database.Set<Folder>().AnyAsync(item => item.SiteId == library.Value && item.ParentId == id, cancellationToken)
            || await database.Set<Asset>().AnyAsync(asset => asset.SiteId == library.Value && asset.FolderId == id && asset.DeletedAt == null, cancellationToken))
        {
            throw new PluginException(MediaErrorCodes.FolderNotEmpty, $"The folder {id:D} holds folders or assets; move or delete them first.");
        }

        database.Remove(folder);
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task<string> GetImageUrlAsync(MediaAsset asset, MediaImageOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);

        var given = options ?? new MediaImageOptions();
        List<ValidationError> errors = [];
        ImageQuery.Check(errors, given, asQuery: false, allowPreset: true);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var parameters = ImageQuery.Of(given);
        var signature = await signing.SignAsync(ImageQuery.Canonical(asset.Id, parameters), cancellationToken);
        var query = string.Join('&', parameters.Append(new(ImageQuery.Signature, signature)).Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value)}"));
        return $"/media/{asset.Id:D}/{Uri.EscapeDataString(asset.FileName)}?{query}";
    }

    public async Task<MediaImage> OpenImageAsync(Guid id, MediaImageOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var (site, library) = await ScopeAsync(Operation(), cancellationToken);
        var reach = await ReachAsync(MediaPermissions.Read, site, library, cancellationToken);
        var asset = await FindAssetAsync(library, id, reach, tracked: false, cancellationToken);
        List<ValidationError> errors = [];
        ImageQuery.Check(errors, options, asQuery: false, allowPreset: false);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var image = await transforms.OpenAsync(site, asset, options, cancellationToken);
        return new MediaImage(image.Content, image.MimeType);
    }

    public async Task<IReadOnlyList<MediaPreset>> ListPresetsAsync(CancellationToken cancellationToken = default)
    {
        var (site, library) = await ScopeAsync(Operation(), cancellationToken);
        await ReachAsync(MediaPermissions.Read, site, library, cancellationToken);
        var presets = await database.Set<Preset>().AsNoTracking().Where(preset => preset.SiteId == library.Value).ToListAsync(cancellationToken);
        return [.. presets.OrderBy(preset => preset.Name, StringComparer.Ordinal).Select(preset => new MediaPreset(preset.Name, MediaTransforms.PresetOptions(preset)))];
    }

    // The parameters are checked as a request's would be, against the limits of the site as they are now.
    public async Task<MediaPreset> SetPresetAsync(string name, MediaImageOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var (site, library) = await ScopeAsync(Operation(), cancellationToken);
        await ManagePresetsAsync(site, library, cancellationToken);
        List<ValidationError> errors = [];
        if (!ImageQuery.IsValidPresetName(name))
        {
            errors.Add(new ValidationError("name", MediaErrorCodes.InvalidField, $"A preset name has from 1 to {ImageQuery.PresetNameMaxLength} lower-case letters, digits and hyphens, and starts with a letter or digit."));
        }

        ImageQuery.Check(errors, options, asQuery: false, allowPreset: false);
        if (options.Format == MediaImageFormat.Avif && !processor.CanEncode("image/avif"))
        {
            errors.Add(new ValidationError("format", MediaErrorCodes.InvalidField, "The image processor does not encode image/avif."));
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        MediaTransforms.CheckLimits(options, await settings.GetAsync(site, cancellationToken));
        var parameters = MediaTransforms.PresetParams(options);
        var preset = await database.Set<Preset>().FirstOrDefaultAsync(item => item.SiteId == library.Value && item.Name == name, cancellationToken);
        if (preset is null)
        {
            database.Add(new Preset { SiteId = library.Value, Name = name, Params = parameters });
        }
        else
        {
            preset.Params = parameters;
        }

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (preset is null)
        {
            // Another writer created it first; this write replaces its parameters, as a second PUT would.
            database.ChangeTracker.Clear();
            await database.Set<Preset>().Where(item => item.SiteId == library.Value && item.Name == name)
                .ExecuteUpdateAsync(item => item.SetProperty(row => row.Params, parameters), CancellationToken.None);
        }

        await cache.EvictAsync([MediaCache.Presets(library.Value)], CancellationToken.None);
        return new MediaPreset(name, MediaTransforms.PresetOptions(new Preset { Params = parameters }));
    }

    public async Task DeletePresetAsync(string name, CancellationToken cancellationToken = default)
    {
        var (site, library) = await ScopeAsync(Operation(), cancellationToken);
        await ManagePresetsAsync(site, library, cancellationToken);
        if (await database.Set<Preset>().Where(preset => preset.SiteId == library.Value && preset.Name == name).ExecuteDeleteAsync(cancellationToken) == 0)
        {
            throw new PluginException(MediaErrorCodes.PresetNotFound, $"The site has no media preset '{name}'.");
        }

        await cache.EvictAsync([MediaCache.Presets(library.Value)], CancellationToken.None);
    }

    // Presets are the site's, so a grant limited to folders does not reach them.
    private async Task ManagePresetsAsync(SiteId site, SiteId library, CancellationToken cancellationToken)
    {
        if (!(await ReachAsync(MediaPermissions.ManagePresets, site, library, cancellationToken)).IsAll)
        {
            throw new PermissionDeniedException(MediaPermissions.ManagePresets, site);
        }
    }

    private Task<FolderReach> ReachAsync(string permission, SiteId site, SiteId library, CancellationToken cancellationToken) =>
        FolderReach.OfAsync(database, permissions, permission, site, library, cancellationToken);

    private Task<List<Guid>> SubtreeAsync(SiteId library, string path, CancellationToken cancellationToken) =>
        FolderReach.SubtreeAsync(database, library, path, cancellationToken);

    private async Task<Asset> FindAssetAsync(SiteId library, Guid id, FolderReach reach, bool tracked, CancellationToken cancellationToken)
    {
        var assets = tracked ? database.Set<Asset>() : database.Set<Asset>().AsNoTracking();
        var asset = await assets.FirstOrDefaultAsync(asset => asset.Id == id && asset.SiteId == library.Value && asset.DeletedAt == null, cancellationToken);
        return asset is not null && reach.Includes(asset.FolderId) ? asset : throw AssetNotFound(id);
    }

    private static PluginException AssetNotFound(Guid id) => new(MediaErrorCodes.AssetNotFound, $"The site has no media asset {id:D}.");

    // The original of a public asset, signed without an expiry, and for an image the processor reads, the presets of the
    // site and, where unsigned URLs are served, a template. A caller that resolves many assets reads the preset names once.
    internal async Task<MediaUrls> UrlsAsync(Asset asset, CancellationToken cancellationToken, IReadOnlyList<string>? presetNames = null)
    {
        var original = await MediaOriginals.UrlAsync(signing, asset.Id, asset.FileName, null, cancellationToken);
        Dictionary<string, string> presets = new(StringComparer.Ordinal);
        string? template = null;
        if (processor.CanDecode(asset.MimeType))
        {
            var info = Info(asset, "");
            var names = presetNames ?? await PresetNamesAsync(asset.SiteId, cancellationToken);
            foreach (var name in names.Order(StringComparer.Ordinal))
            {
                presets[name] = await GetImageUrlAsync(info, new MediaImageOptions { Preset = name }, cancellationToken);
            }

            if (!options.Value.RequireSignedUrls)
            {
                template = $"/media/{asset.Id:D}/{Uri.EscapeDataString(asset.FileName)}?{ImageQuery.Width}={{w}}&{ImageQuery.Height}={{h}}&{ImageQuery.Fit}={{fit}}&{ImageQuery.Quality}={{q}}&{ImageQuery.Format}={{f}}";
            }
        }

        return new MediaUrls(original, presets, template);
    }

    internal Task<List<string>> PresetNamesAsync(Guid site, CancellationToken cancellationToken) =>
        database.Set<Preset>().AsNoTracking().Where(preset => preset.SiteId == site).Select(preset => preset.Name).ToListAsync(cancellationToken);

    private async Task<Folder> FindFolderAsync(SiteId library, Guid id, FolderReach reach, bool tracked, CancellationToken cancellationToken)
    {
        var folders = tracked ? database.Set<Folder>() : database.Set<Folder>().AsNoTracking();
        var folder = await folders.FirstOrDefaultAsync(folder => folder.Id == id && folder.SiteId == library.Value, cancellationToken);
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

    private async Task ForgetReplacedAsync(Guid id, string provider, string key)
    {
        if (storages.Find(provider) is not { } storage)
        {
            MediaLog.ReplacedNotDeleted(logger, id, provider, key, null);
            return;
        }

        try
        {
            await storage.DeleteAsync(key, CancellationToken.None);
        }
        catch (Exception exception)
        {
            MediaLog.ReplacedNotDeleted(logger, id, provider, key, exception);
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

    // The site of the request, whose roles and settings decide what the caller may do, and the site whose library it acts on.
    private async ValueTask<(SiteId Site, SiteId Library)> ScopeAsync(OperationContext operation, CancellationToken cancellationToken) =>
        (operation.Site, await libraries.OfAsync(operation.Site, cancellationToken));

    private OperationContext Operation() =>
        operations.Current ?? throw new InvalidOperationException("The media of a site is managed inside an operation, which names the site.");

    internal static MediaAsset Info(Asset asset, string locale) => new()
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

    // Every folder and the root when a grant has no folder limit, otherwise the folders the grants name and their subfolders.
    // No grant at all is a refusal.
    public static async Task<FolderReach> OfAsync(PluginDbContext<MediaPlugin> database, IPermissionChecker permissions, string permission, SiteId site, SiteId library, CancellationToken cancellationToken)
    {
        var resources = await permissions.ResourcesAsync(permission, site, cancellationToken);
        if (resources is null)
        {
            return All;
        }

        if (resources.Count == 0)
        {
            throw new PermissionDeniedException(permission, site);
        }

        var named = resources.Select(resource => Guid.TryParse(resource, out var id) ? id : (Guid?)null).OfType<Guid>().ToList();
        var paths = await database.Set<Folder>().AsNoTracking()
            .Where(folder => folder.SiteId == library.Value && named.Contains(folder.Id))
            .Select(folder => folder.Path)
            .ToListAsync(cancellationToken);

        HashSet<Guid> folders = [];
        foreach (var path in paths)
        {
            folders.UnionWith(await SubtreeAsync(database, library, path, cancellationToken));
        }

        return new FolderReach(folders);
    }

    public static Task<List<Guid>> SubtreeAsync(PluginDbContext<MediaPlugin> database, SiteId library, string path, CancellationToken cancellationToken) =>
        database.Set<Folder>().AsNoTracking().Where(folder => folder.SiteId == library.Value && folder.Path.StartsWith(path)).Select(folder => folder.Id).ToListAsync(cancellationToken);
}
