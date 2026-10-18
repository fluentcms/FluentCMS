using System.Text.Json;
using FluentCMS.Core.Audit;
using FluentCMS.Core.Content;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Infrastructure.Content;

// Expected failures are found before the unit of work begins, so they leave a caller's outer unit of work able to commit.
// Reads never track rows, so a long-lived context (a Blazor circuit) always sees what is stored, not what it saw before.
internal sealed class EntryDirectory(
    FluentCmsDbContext context,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    IContentTypeDirectory contentTypes,
    FieldTypeRegistry fieldTypes,
    TimeProvider clock) : IEntryDirectory
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
            .Where(entry => entry.SiteId == SiteId && entry.ContentTypeId == typeId)
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

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);

        // Moves the pointer only from the version the caller read; a concurrent update that committed first leaves nothing to move.
        var moved = await context.Entries
            .Where(row => row.Id == id && row.LatestVersion == baseVersion)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.LatestVersion, entry.LatestVersion), cancellationToken);
        if (moved == 0)
        {
            return Result<Entry>.Failure([VersionConflict(id, baseVersion)]);
        }

        context.EntryVersions.Add(VersionRow(entry.Latest));
        await auditLog.AppendAsync(
            new NewAuditEvent("entry.updated") { SubjectType = SubjectType, SubjectId = id, PreviousVersionId = baseRow.Id },
            cancellationToken);

        return await CommitAsync(scope, entry, VersionConflict(id, baseVersion), cancellationToken);
    }

    private async Task<StoredEntry?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        return await (
            from entry in context.Entries.AsNoTracking()
            join type in context.ContentTypes.AsNoTracking() on entry.ContentTypeId equals type.Id
            where entry.Id == id && entry.SiteId == SiteId
            select new StoredEntry(entry, type.Slug))
            .SingleOrDefaultAsync(cancellationToken);
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

    private static Error VersionConflict(Guid id, int baseVersion)
    {
        return new Error(
            ContentErrorCodes.VersionConflict,
            $"Version {baseVersion} is not the latest version of entry {id}; read it again and update the latest version.");
    }

    private sealed record StoredEntry(EntryRow Row, string ContentType);
}
