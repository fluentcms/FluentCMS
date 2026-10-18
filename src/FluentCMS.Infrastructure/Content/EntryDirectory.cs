using System.Text.Json;
using FluentCMS.Core.Audit;
using FluentCMS.Core.Content;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FluentCMS.Infrastructure.Content;

// Expected failures are found before the unit of work begins, so they leave a caller's outer unit of work able to commit.
// Reads never track rows, so a long-lived context (a Blazor circuit) always sees what is stored, not what it saw before.
internal sealed class EntryDirectory(
    FluentCmsDbContext context,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    IContentTypeDirectory contentTypes,
    FieldTypeRegistry fieldTypes,
    TimeProvider clock,
    IOptions<ContentOptions> options) : IEntryDirectory
{
    private const string SubjectType = "entry";

    // Until multi-site (MST-01), every entry belongs to the one site (SCH-07).
    private static readonly Guid SiteId = Site.Default.Id;

    public async Task<Result<IReadOnlyList<EntrySummary>>> ListAsync(string contentType, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contentType);

        var typeId = await context.ContentTypes
            .AsNoTracking()
            .Where(type => type.SiteId == SiteId && type.Slug == contentType)
            .Select(type => (Guid?)type.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (typeId is null)
        {
            return Result<IReadOnlyList<EntrySummary>>.Failure([ContentTypeNotFound(contentType)]);
        }

        var entries = await context.Entries
            .AsNoTracking()
            .Where(entry => entry.SiteId == SiteId && entry.ContentTypeId == typeId && entry.DeletedAt == null)
            .OrderBy(entry => entry.Id)
            .Select(entry => new EntrySummary(entry.Id, entry.LatestVersion, entry.PublishedVersion))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<EntrySummary>>.Success(entries);
    }

    public async Task<Result<Entry>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var stored = await FindAsync(id, cancellationToken);
        if (stored is null)
        {
            return Result<Entry>.Failure([EntryNotFound(id)]);
        }

        var latest = await VersionRowAsync(id, stored.Row.LatestVersion, cancellationToken)
            ?? throw new InvalidOperationException($"Entry {id} points at version {stored.Row.LatestVersion}, which is not stored.");

        return Result<Entry>.Success(ToEntry(stored, latest));
    }

    public async Task<Result<EntryVersion>> GetVersionAsync(Guid id, int version, CancellationToken cancellationToken = default)
    {
        var stored = await FindAsync(id, cancellationToken);
        if (stored is null)
        {
            return Result<EntryVersion>.Failure([EntryNotFound(id)]);
        }

        var row = await VersionRowAsync(id, version, cancellationToken);

        return row is null
            ? Result<EntryVersion>.Failure(
                ContentErrorCodes.VersionNotFound,
                $"Entry {id} has no version {version}; its latest version is {stored.Row.LatestVersion}.")
            : Result<EntryVersion>.Success(ToVersion(row));
    }

    public async Task<Result<IReadOnlyList<EntryVersionSummary>>> HistoryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (await FindAsync(id, cancellationToken) is null)
        {
            return Result<IReadOnlyList<EntryVersionSummary>>.Failure([EntryNotFound(id)]);
        }

        var versions = await context.EntryVersions
            .AsNoTracking()
            .Where(version => version.EntryId == id)
            .OrderBy(version => version.Version)
            .Select(version => new EntryVersionSummary(version.Version, version.ContentTypeVersion, version.CreatedAt))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<EntryVersionSummary>>.Success(versions);
    }

    public async Task<Result<Entry>> CreateAsync(NewEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(entry.ContentType);

        var type = await contentTypes.GetAsync(entry.ContentType, cancellationToken);
        if (!type.IsSuccess)
        {
            return Result<Entry>.Failure(type.Errors);
        }

        var created = Entry.Create(type.Value, entry.Fields, fieldTypes, clock.GetUtcNow());
        if (!created.IsSuccess)
        {
            return created;
        }

        var stored = created.Value;
        var references = EntryFields.References(stored.Latest.Fields, type.Value.Definition, fieldTypes);
        var referenceErrors = await ReferenceErrorsAsync(references, cancellationToken);
        if (referenceErrors.Count > 0)
        {
            return Result<Entry>.Failure(referenceErrors);
        }

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);

        context.Entries.Add(new EntryRow
        {
            Id = stored.Id,
            SiteId = stored.SiteId,
            ContentTypeId = stored.ContentTypeId,
            LatestVersion = stored.LatestVersion,
            PublishedVersion = stored.PublishedVersion,
        });
        context.EntryVersions.Add(VersionRow(stored.Latest));
        context.EntryReferences.AddRange(ReferenceRows(stored.Latest, references));
        await auditLog.AppendAsync(new NewAuditEvent("entry.created") { SubjectType = SubjectType, SubjectId = stored.Id }, cancellationToken);

        // A new id conflicts with nothing, so no unique index can turn this commit into an expected failure.
        await scope.CommitAsync(cancellationToken);
        return created;
    }

    public async Task<Result<Entry>> UpdateAsync(Guid id, int baseVersion, JsonElement fields, CancellationToken cancellationToken = default)
    {
        var stored = await FindAsync(id, cancellationToken);
        if (stored is null)
        {
            return Result<Entry>.Failure([EntryNotFound(id)]);
        }

        if (stored.Row.LatestVersion != baseVersion)
        {
            return Result<Entry>.Failure([VersionConflict(id, baseVersion)]);
        }

        // Checked against the type as it is now, so a field added since the last write is filled in or reported.
        var type = await contentTypes.GetAsync(stored.ContentType, cancellationToken);
        if (!type.IsSuccess)
        {
            return Result<Entry>.Failure(type.Errors);
        }

        var baseRow = await VersionRowAsync(id, baseVersion, cancellationToken)
            ?? throw new InvalidOperationException($"Entry {id} points at version {baseVersion}, which is not stored.");

        var revised = ToEntry(stored, baseRow).Revise(type.Value, fields, fieldTypes, clock.GetUtcNow());
        if (!revised.IsSuccess)
        {
            return revised;
        }

        var entry = revised.Value;
        var references = EntryFields.References(entry.Latest.Fields, type.Value.Definition, fieldTypes);
        var referenceErrors = await ReferenceErrorsAsync(references, cancellationToken);
        if (referenceErrors.Count > 0)
        {
            return Result<Entry>.Failure(referenceErrors);
        }

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);

        // Moves the pointer only from the version the caller read; a concurrent update that committed first leaves nothing to move.
        var moved = await context.Entries
            .Where(row => row.Id == id && row.LatestVersion == baseVersion && row.DeletedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.LatestVersion, entry.LatestVersion), cancellationToken);
        if (moved == 0)
        {
            return Result<Entry>.Failure([VersionConflict(id, baseVersion)]);
        }

        context.EntryVersions.Add(VersionRow(entry.Latest));
        context.EntryReferences.AddRange(ReferenceRows(entry.Latest, references));
        await auditLog.AppendAsync(
            new NewAuditEvent("entry.updated") { SubjectType = SubjectType, SubjectId = id, PreviousVersionId = baseRow.Id },
            cancellationToken);

        return await CommitAsync(scope, entry, VersionConflict(id, baseVersion), cancellationToken);
    }

    public async Task<Result> DeleteAsync(Guid id, int baseVersion, CancellationToken cancellationToken = default)
    {
        var stored = await FindAsync(id, cancellationToken);
        if (stored is null)
        {
            return Result.Failure([EntryNotFound(id)]);
        }

        if (stored.Row.LatestVersion != baseVersion)
        {
            return Result.Failure([VersionConflict(id, baseVersion)]);
        }

        var latest = await VersionRowAsync(id, baseVersion, cancellationToken)
            ?? throw new InvalidOperationException($"Entry {id} points at version {baseVersion}, which is not stored.");

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);

        // Only from the version the caller read, and only once: a concurrent update or delete that committed first wins.
        var deletedAt = clock.GetUtcNow();
        var deleted = await context.Entries
            .Where(row => row.Id == id && row.LatestVersion == baseVersion && row.DeletedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.DeletedAt, deletedAt), cancellationToken);
        if (deleted == 0)
        {
            return Result.Failure([VersionConflict(id, baseVersion)]);
        }

        await auditLog.AppendAsync(
            new NewAuditEvent("entry.deleted") { SubjectType = SubjectType, SubjectId = id, PreviousVersionId = latest.Id },
            cancellationToken);
        await scope.CommitAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result<Entry>> RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var stored = await FindAsync(id, cancellationToken, includeDeleted: true);
        if (stored is null)
        {
            return Result<Entry>.Failure([EntryNotFound(id)]);
        }

        if (stored.Row.DeletedAt is not { } deletedAt)
        {
            return Result<Entry>.Failure([EntryNotDeleted(id)]);
        }

        var until = deletedAt + options.Value.DeletedRetention;
        if (clock.GetUtcNow() >= until)
        {
            return Result<Entry>.Failure(
                ContentErrorCodes.RetentionExpired,
                $"Entry {id} was deleted at {deletedAt:O} and could be restored until {until:O}; that window has closed.");
        }

        var latest = await VersionRowAsync(id, stored.Row.LatestVersion, cancellationToken)
            ?? throw new InvalidOperationException($"Entry {id} points at version {stored.Row.LatestVersion}, which is not stored.");

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);

        // A concurrent restore that committed first leaves nothing to restore.
        var restored = await context.Entries
            .Where(row => row.Id == id && row.DeletedAt != null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.DeletedAt, (DateTimeOffset?)null), cancellationToken);
        if (restored == 0)
        {
            return Result<Entry>.Failure([EntryNotDeleted(id)]);
        }

        await auditLog.AppendAsync(new NewAuditEvent("entry.restored") { SubjectType = SubjectType, SubjectId = id }, cancellationToken);
        await scope.CommitAsync(cancellationToken);

        return Result<Entry>.Success(ToEntry(stored, latest));
    }

    public async Task<IReadOnlyList<DeletedEntry>> ListDeletedAsync(CancellationToken cancellationToken = default)
    {
        var rows = await (
            from entry in context.Entries.AsNoTracking()
            join type in context.ContentTypes.AsNoTracking() on entry.ContentTypeId equals type.Id
            where entry.SiteId == SiteId && entry.DeletedAt != null
            select new { entry.Id, type.Slug, entry.LatestVersion, entry.DeletedAt })
            .ToListAsync(cancellationToken);

        // Ordered here, because SQLite cannot order by the stored offsets.
        return rows
            .Select(row => new DeletedEntry(row.Id, row.Slug, row.LatestVersion, row.DeletedAt!.Value, row.DeletedAt.Value + options.Value.DeletedRetention))
            .OrderByDescending(entry => entry.DeletedAt)
            .ThenBy(entry => entry.Id)
            .ToList();
    }

    private async Task<StoredEntry?> FindAsync(Guid id, CancellationToken cancellationToken, bool includeDeleted = false)
    {
        return await (
            from entry in context.Entries.AsNoTracking()
            join type in context.ContentTypes.AsNoTracking() on entry.ContentTypeId equals type.Id
            where entry.Id == id && entry.SiteId == SiteId && (includeDeleted || entry.DeletedAt == null)
            select new StoredEntry(entry, type.Slug))
            .SingleOrDefaultAsync(cancellationToken);
    }

    // Every entry a version points at must exist in the site and be of the type its field names (CNT-03). Media assets are checked
    // once the library exists (F-MED).
    private async Task<List<Error>> ReferenceErrorsAsync(IReadOnlyList<ValueReference> references, CancellationToken cancellationToken)
    {
        var entries = references.Where(reference => reference.Kind == ReferenceKind.Entry).ToList();
        if (entries.Count == 0)
        {
            return [];
        }

        var ids = entries.Select(reference => reference.TargetId).Distinct().ToList();
        var types = await (
            from entry in context.Entries.AsNoTracking()
            join type in context.ContentTypes.AsNoTracking() on entry.ContentTypeId equals type.Id
            where entry.SiteId == SiteId && ids.Contains(entry.Id) && entry.DeletedAt == null
            select new { entry.Id, type.Slug })
            .ToDictionaryAsync(found => found.Id, found => found.Slug, cancellationToken);

        var errors = new List<Error>();
        foreach (var reference in entries)
        {
            var field = reference.Pointer.Split('/')[1];
            var wanted = reference.Target!.Type;

            if (!types.TryGetValue(reference.TargetId, out var slug))
            {
                errors.Add(new Error(
                    ContentErrorCodes.ReferenceNotFound,
                    $"Field '{field}': {reference.Pointer} points at {reference.TargetId}, which is not an entry in this site."));
            }
            else if (slug != wanted)
            {
                errors.Add(new Error(
                    ContentErrorCodes.ReferenceTypeMismatch,
                    $"Field '{field}': {reference.Pointer} points at {reference.TargetId}, which is a '{slug}', but the field references '{wanted}'."));
            }
        }

        return errors;
    }

    private Task<EntryVersionRow?> VersionRowAsync(Guid id, int version, CancellationToken cancellationToken)
    {
        return context.EntryVersions
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.EntryId == id && row.Version == version, cancellationToken);
    }

    private static async Task<Result<Entry>> CommitAsync(IUnitOfWorkScope scope, Entry entry, Error conflict, CancellationToken cancellationToken)
    {
        try
        {
            await scope.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (DatabaseErrors.IsUniqueViolation(exception))
        {
            // The scope rolls back when the caller disposes it.
            return Result<Entry>.Failure([conflict]);
        }

        return Result<Entry>.Success(entry);
    }

    private static EntryVersionRow VersionRow(EntryVersion version)
    {
        return new EntryVersionRow
        {
            Id = Guid.CreateVersion7(version.CreatedAt),
            EntryId = version.EntryId,
            Version = version.Version,
            ContentTypeVersion = version.ContentTypeVersion,
            Fields = version.Fields.GetRawText(),
            CreatedAt = version.CreatedAt,
        };
    }

    private static IEnumerable<EntryReferenceRow> ReferenceRows(EntryVersion version, IReadOnlyList<ValueReference> references)
    {
        return references.Select(reference => new EntryReferenceRow
        {
            Id = Guid.CreateVersion7(version.CreatedAt),
            EntryId = version.EntryId,
            Version = version.Version,
            Path = reference.Pointer,
            TargetKind = reference.Kind,
            TargetId = reference.TargetId,
            TargetField = reference.Target?.Field,
        });
    }

    private static Entry ToEntry(StoredEntry stored, EntryVersionRow latest)
    {
        var row = stored.Row;
        return Entry.Restore(row.Id, row.SiteId, row.ContentTypeId, stored.ContentType, row.PublishedVersion, ToVersion(latest));
    }

    private static EntryVersion ToVersion(EntryVersionRow row)
    {
        using var document = JsonDocument.Parse(row.Fields);
        return new EntryVersion(row.EntryId, row.Version, row.ContentTypeVersion, document.RootElement, row.CreatedAt);
    }

    private static Error ContentTypeNotFound(string slug)
    {
        return new Error(SchemaErrorCodes.ContentTypeNotFound, $"There is no content type '{slug}' in this site.");
    }

    private static Error EntryNotFound(Guid id)
    {
        return new Error(ContentErrorCodes.EntryNotFound, $"There is no entry {id} in this site.");
    }

    private static Error EntryNotDeleted(Guid id)
    {
        return new Error(ContentErrorCodes.EntryNotDeleted, $"Entry {id} is not deleted, so there is nothing to restore.");
    }

    private static Error VersionConflict(Guid id, int baseVersion)
    {
        return new Error(
            ContentErrorCodes.VersionConflict,
            $"Version {baseVersion} is not the latest version of entry {id}; read it again and update the latest version.");
    }

    private sealed record StoredEntry(EntryRow Row, string ContentType);
}
