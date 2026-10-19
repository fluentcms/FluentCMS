using FluentCMS.Core.Audit;
using FluentCMS.Core.Media;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FluentCMS.Infrastructure.Media;

// Expected failures are found before the unit of work begins, so they leave a caller's outer unit of work able to commit. Reads
// never track rows, so a long-lived context always sees what is stored.
internal sealed class MediaLibrary(
    FluentCmsDbContext context,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    MediaStorageRegistry storages,
    IOptions<MediaOptions> options,
    TimeProvider clock) : IMediaLibrary
{
    private const string SubjectType = "media";

    // Until multi-site (MST-01), every asset belongs to the one site (SCH-07).
    private static readonly Guid SiteId = Site.Default.Id;

    public long MaxBytes => options.Value.MaxBytes;

    public async Task<Result<MediaAsset>> UploadAsync(NewAsset asset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);

        var errors = new List<Error>();
        var metadata = Revalidate(asset.Metadata, errors);
        var contentType = asset.ContentType?.Trim().ToLowerInvariant();
        if (!MediaTypes.IsAllowed(contentType))
        {
            errors.Add(new Error(
                MediaErrorCodes.ContentTypeInvalid,
                $"contentType: '{asset.ContentType}' is not a media type uploads accept; use one of {string.Join(", ", MediaTypes.Allowed)}."));
        }

        if (errors.Count > 0)
        {
            return Result<MediaAsset>.Failure(errors);
        }

        // The file's start is read first, so a file that is not what it claims to be never reaches the storage.
        var head = new byte[MediaTypes.SignatureLength];
        var headLength = await asset.Content.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, cancellationToken);
        if (headLength == 0)
        {
            return Result<MediaAsset>.Failure(MediaErrorCodes.MediaEmpty, "The file is empty; upload the bytes of the asset.");
        }

        if (!MediaTypes.Matches(contentType!, head.AsSpan(0, headLength)))
        {
            return Result<MediaAsset>.Failure(
                MediaErrorCodes.ContentMismatch,
                $"The file does not start like {contentType}; upload it with the media type of its content.");
        }

        var storage = Storage();
        var limit = options.Value.MaxBytes;

        // The storage sees at most one byte past the limit, so an oversized upload costs no more than the limit to refuse.
        var stored = await storage.SaveAsync(new CappedStream(new PrefixedStream(head.AsMemory(0, headLength), asset.Content), limit + 1), cancellationToken);
        if (stored.Length == 0 || stored.Length > limit)
        {
            await storage.DeleteAsync(stored.Key, CancellationToken.None);
            return stored.Length == 0
                ? Result<MediaAsset>.Failure(MediaErrorCodes.MediaEmpty, "The file is empty; upload the bytes of the asset.")
                : Result<MediaAsset>.Failure(MediaErrorCodes.MediaTooLarge, $"The file has more than {limit} bytes, and an upload may have at most {limit}.");
        }

        var now = clock.GetUtcNow();
        var id = Guid.CreateVersion7(now);
        var version = new MediaAssetVersion(id, 1, new MediaBlob(contentType!, stored.Length, stored.Sha256, storage.Name, stored.Key), metadata!, now);

        try
        {
            await using var scope = await unitOfWork.BeginAsync(cancellationToken);

            context.MediaAssets.Add(new MediaAssetRow { Id = id, SiteId = SiteId, LatestVersion = 1 });
            context.MediaAssetVersions.Add(VersionRow(version));
            await auditLog.AppendAsync(new NewAuditEvent("media.uploaded") { SubjectType = SubjectType, SubjectId = id }, cancellationToken);

            await scope.CommitAsync(cancellationToken);
        }
        catch
        {
            // Bytes without an asset row are never read; deleting them keeps the storage from filling with them.
            await storage.DeleteAsync(stored.Key, CancellationToken.None);
            throw;
        }

        return Result<MediaAsset>.Success(new MediaAsset(id, SiteId, 1, null, version));
    }

    public async Task<Result<MediaAsset>> UpdateAsync(Guid id, int baseVersion, AssetMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var row = await FindAsync(id, cancellationToken);
        if (row is null)
        {
            return Result<MediaAsset>.Failure([NotFound(id)]);
        }

        if (row.LatestVersion != baseVersion)
        {
            return Result<MediaAsset>.Failure([Conflict(id, baseVersion)]);
        }

        var errors = new List<Error>();
        var checkedMetadata = Revalidate(metadata, errors);
        if (errors.Count > 0)
        {
            return Result<MediaAsset>.Failure(errors);
        }

        var baseRow = await VersionRowAsync(id, baseVersion, cancellationToken)
            ?? throw new InvalidOperationException($"Media asset {id} points at version {baseVersion}, which is not stored.");
        var version = baseRow.ToVersion() with { Version = baseVersion + 1, Metadata = checkedMetadata!, CreatedAt = clock.GetUtcNow() };

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);

        // Moves the pointer only from the version the caller read; a concurrent update that committed first leaves nothing to move.
        var moved = await context.MediaAssets
            .Where(asset => asset.Id == id && asset.LatestVersion == baseVersion)
            .ExecuteUpdateAsync(setters => setters.SetProperty(asset => asset.LatestVersion, version.Version), cancellationToken);
        if (moved == 0)
        {
            return Result<MediaAsset>.Failure([Conflict(id, baseVersion)]);
        }

        context.MediaAssetVersions.Add(VersionRow(version));
        await auditLog.AppendAsync(
            new NewAuditEvent("media.updated") { SubjectType = SubjectType, SubjectId = id, PreviousVersionId = baseRow.Id },
            cancellationToken);

        try
        {
            await scope.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (DatabaseErrors.IsUniqueViolation(exception))
        {
            return Result<MediaAsset>.Failure([Conflict(id, baseVersion)]);
        }

        return Result<MediaAsset>.Success(new MediaAsset(id, row.SiteId, version.Version, row.PublishedVersion, version));
    }

    public async Task<Result<MediaAsset>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await FindAsync(id, cancellationToken);
        if (row is null)
        {
            return Result<MediaAsset>.Failure([NotFound(id)]);
        }

        var latest = await VersionRowAsync(id, row.LatestVersion, cancellationToken)
            ?? throw new InvalidOperationException($"Media asset {id} points at version {row.LatestVersion}, which is not stored.");

        return Result<MediaAsset>.Success(new MediaAsset(id, row.SiteId, row.LatestVersion, row.PublishedVersion, latest.ToVersion()));
    }

    public async Task<Result<MediaAssetVersion>> GetVersionAsync(Guid id, int version, CancellationToken cancellationToken = default)
    {
        var row = await FindAsync(id, cancellationToken);
        if (row is null)
        {
            return Result<MediaAssetVersion>.Failure([NotFound(id)]);
        }

        var stored = await VersionRowAsync(id, version, cancellationToken);
        return stored is null
            ? Result<MediaAssetVersion>.Failure(
                MediaErrorCodes.VersionNotFound,
                $"Media asset {id} has no version {version}; its latest version is {row.LatestVersion}.")
            : Result<MediaAssetVersion>.Success(stored.ToVersion());
    }

    public async Task<IReadOnlyList<MediaAssetSummary>> ListAsync(MediaQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var latest = LatestVersions();
        if (query.Folder is { } folder)
        {
            // Folders are stored in lowercase, so both providers compare them alike whatever case the query uses.
            var path = folder.Trim().Trim('/').ToLowerInvariant();
            var prefix = path + "/";
            latest = query.IncludeSubfolders
                ? latest.Where(version => path.Length == 0 || version.Folder == path || version.Folder.StartsWith(prefix))
                : latest.Where(version => version.Folder == path);
        }

        if (query.Tag is { } tag)
        {
            var wanted = tag.Trim().ToLowerInvariant();
            latest = latest.Where(version => version.Tags.Contains(wanted));
        }

        if (query.Text?.Trim().ToLowerInvariant() is { Length: > 0 } text)
        {
            latest = latest.Where(version => version.FileName.ToLower().Contains(text) || version.Folder.Contains(text));
        }

        var joined =
            from version in latest
            join asset in context.MediaAssets.AsNoTracking() on version.AssetId equals asset.Id
            select new { asset.Id, asset.PublishedVersion, Version = version };
        var rows = await (query.Newest is { } newest
            ? joined.OrderByDescending(row => row.Id).Take(newest)
            : joined.OrderBy(row => row.Id))
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new MediaAssetSummary(
                row.Version.AssetId,
                row.Version.Version,
                row.PublishedVersion,
                row.Version.FileName,
                row.Version.ContentType,
                row.Version.Length,
                row.Version.Folder,
                row.Version.Tags,
                row.Version.AltText != null))
            .ToList();
    }

    public async Task<IReadOnlyList<string>> FoldersAsync(CancellationToken cancellationToken = default)
    {
        var folders = await LatestVersions().Select(version => version.Folder).Distinct().ToListAsync(cancellationToken);
        return folders.Order(StringComparer.Ordinal).ToList();
    }

    public async Task<Result<MediaContent>> OpenAsync(Guid id, int? version = null, CancellationToken cancellationToken = default)
    {
        var row = await FindAsync(id, cancellationToken);
        if (row is null)
        {
            return Result<MediaContent>.Failure([NotFound(id)]);
        }

        var number = version ?? row.LatestVersion;
        var stored = await VersionRowAsync(id, number, cancellationToken);
        if (stored is null)
        {
            return Result<MediaContent>.Failure(
                MediaErrorCodes.VersionNotFound,
                $"Media asset {id} has no version {number}; its latest version is {row.LatestVersion}.");
        }

        var content = storages.Find(stored.Storage) is { } storage ? await storage.OpenReadAsync(stored.StorageKey, cancellationToken) : null;
        return content is null
            ? Result<MediaContent>.Failure(
                MediaErrorCodes.ContentMissing,
                $"The bytes of media asset {id} are not in the storage '{stored.Storage}'; it may no longer be registered or may have lost them.")
            : Result<MediaContent>.Success(new MediaContent(content, stored.ContentType, stored.FileName, stored.Length));
    }

    // The latest version of every asset of the site, which lists and folders read.
    private IQueryable<MediaAssetVersionRow> LatestVersions()
    {
        return
            from version in context.MediaAssetVersions.AsNoTracking()
            join asset in context.MediaAssets.AsNoTracking() on version.AssetId equals asset.Id
            where asset.SiteId == SiteId && version.Version == asset.LatestVersion
            select version;
    }

    private IMediaStorage Storage()
    {
        // The host checks the name at startup (MediaOptionsValidation), so a missing storage here is a misconfigured service provider.
        return storages.Find(options.Value.Storage)
            ?? throw new InvalidOperationException($"Media__Storage is '{options.Value.Storage}', which is no registered media storage.");
    }

    private Task<MediaAssetRow?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        return context.MediaAssets.AsNoTracking().SingleOrDefaultAsync(asset => asset.Id == id && asset.SiteId == SiteId, cancellationToken);
    }

    private Task<MediaAssetVersionRow?> VersionRowAsync(Guid id, int version, CancellationToken cancellationToken)
    {
        return context.MediaAssetVersions.AsNoTracking().SingleOrDefaultAsync(row => row.AssetId == id && row.Version == version, cancellationToken);
    }

    // Metadata built outside AssetMetadata.Create is checked again, so the stored form is always the normalized one.
    private static AssetMetadata? Revalidate(AssetMetadata metadata, List<Error> errors)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var created = AssetMetadata.Create(metadata.FileName, metadata.Folder, metadata.Tags, metadata.AltText, metadata.Caption);
        errors.AddRange(created.Errors);
        return created.IsSuccess ? created.Value : null;
    }

    private static MediaAssetVersionRow VersionRow(MediaAssetVersion version)
    {
        return new MediaAssetVersionRow
        {
            Id = Guid.CreateVersion7(version.CreatedAt),
            AssetId = version.AssetId,
            Version = version.Version,
            FileName = version.Metadata.FileName,
            ContentType = version.Blob.ContentType,
            Length = version.Blob.Length,
            Sha256 = version.Blob.Sha256,
            Storage = version.Blob.Storage,
            StorageKey = version.Blob.Key,
            Folder = version.Metadata.Folder,
            Tags = [.. version.Metadata.Tags],
            AltText = version.Metadata.AltText,
            Caption = version.Metadata.Caption,
            CreatedAt = version.CreatedAt,
        };
    }

    private static Error NotFound(Guid id)
    {
        return new Error(MediaErrorCodes.AssetNotFound, $"There is no media asset {id} in this site.");
    }

    private static Error Conflict(Guid id, int baseVersion)
    {
        return new Error(
            MediaErrorCodes.VersionConflict,
            $"Version {baseVersion} is not the latest version of media asset {id}; read it again and update the latest version.");
    }
}
