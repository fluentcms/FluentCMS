using FluentCMS.Core.Audit;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Infrastructure.Schema;

// Reads never track rows, so a long-lived context (a Blazor circuit) always sees what is stored, not what it saw before.
internal sealed class ContentTypeDirectory(
    FluentCmsDbContext context,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    FieldTypeRegistry fieldTypes) : IContentTypeDirectory
{
    private const string SubjectType = "content_type";

    // Until multi-site (MST-01), every type belongs to the one site (SCH-07).
    private static readonly Guid SiteId = Site.Default.Id;

    public async Task<IReadOnlyList<ContentTypeSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await context.ContentTypes
            .AsNoTracking()
            .Where(type => type.SiteId == SiteId)
            .OrderBy(type => type.Slug)
            .Select(type => new ContentTypeSummary(type.Id, type.Name, type.Slug, type.Version))
            .ToListAsync(cancellationToken);
    }

    public Task<Result<ContentType>> GetAsync(string slug, CancellationToken cancellationToken = default)
    {
        return ReadAsync(slug, version: null, cancellationToken);
    }

    public Task<Result<ContentType>> GetVersionAsync(string slug, int version, CancellationToken cancellationToken = default)
    {
        return ReadAsync(slug, version, cancellationToken);
    }

    public async Task<Result<ContentType>> CreateAsync(NewContentType contentType, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contentType);

        var created = ContentType.Create(SiteId, contentType.Name, contentType.Slug, contentType.Definition, fieldTypes);
        if (!created.IsSuccess)
        {
            return created;
        }

        var type = created.Value;
        var errors = new List<Error>();

        // Checked before the unit of work begins, so an expected failure leaves a caller's outer unit of work able to commit.
        if (await context.ContentTypes.AnyAsync(row => row.SiteId == SiteId && row.Slug == type.Slug, cancellationToken))
        {
            errors.Add(SlugTaken(type.Slug));
        }

        errors.AddRange(await UnknownTargetsAsync(type, cancellationToken));
        if (errors.Count > 0)
        {
            return Result<ContentType>.Failure(errors);
        }

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);

        context.ContentTypes.Add(new ContentTypeRow
        {
            Id = type.Id,
            SiteId = type.SiteId,
            Name = type.Name,
            Slug = type.Slug,
            Version = type.Version,
        });
        context.ContentTypeVersions.Add(VersionRow(type));
        await auditLog.AppendAsync(new NewAuditEvent("content_type.created") { SubjectType = SubjectType, SubjectId = type.Id }, cancellationToken);

        return await CommitAsync(scope, type, SlugTaken(type.Slug), cancellationToken);
    }

    public async Task<Result<ContentType>> ReviseAsync(
        string slug,
        int baseVersion,
        ContentTypeDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(slug);
        ArgumentNullException.ThrowIfNull(definition);

        var row = await FindAsync(slug, cancellationToken);
        if (row is null)
        {
            return NotFound(slug);
        }

        if (row.Version != baseVersion)
        {
            return Result<ContentType>.Failure([VersionConflict(slug, baseVersion)]);
        }

        var current = await context.ContentTypeVersions
            .AsNoTracking()
            .SingleAsync(version => version.ContentTypeId == row.Id && version.Version == row.Version, cancellationToken);

        var revised = ToContentType(row, current).Revise(definition, fieldTypes);
        if (!revised.IsSuccess)
        {
            return revised;
        }

        var type = revised.Value;
        var unknownTargets = await UnknownTargetsAsync(type, cancellationToken);
        if (unknownTargets.Count > 0)
        {
            return Result<ContentType>.Failure(unknownTargets);
        }

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);

        // Moves the pointer only from the version the caller read; a concurrent revision that committed first leaves nothing to move.
        var moved = await context.ContentTypes
            .Where(stored => stored.Id == row.Id && stored.Version == baseVersion)
            .ExecuteUpdateAsync(setters => setters.SetProperty(stored => stored.Version, type.Version), cancellationToken);
        if (moved == 0)
        {
            return Result<ContentType>.Failure([VersionConflict(slug, baseVersion)]);
        }

        context.ContentTypeVersions.Add(VersionRow(type));
        await auditLog.AppendAsync(
            new NewAuditEvent("content_type.revised") { SubjectType = SubjectType, SubjectId = type.Id, PreviousVersionId = current.Id },
            cancellationToken);

        return await CommitAsync(scope, type, VersionConflict(slug, baseVersion), cancellationToken);
    }

    private async Task<Result<ContentType>> ReadAsync(string slug, int? version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(slug);

        var row = await FindAsync(slug, cancellationToken);
        if (row is null)
        {
            return NotFound(slug);
        }

        var number = version ?? row.Version;
        var stored = await context.ContentTypeVersions
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.ContentTypeId == row.Id && candidate.Version == number, cancellationToken);

        return stored is null
            ? Result<ContentType>.Failure(
                SchemaErrorCodes.VersionNotFound,
                $"The content type '{slug}' has no version {number}; its current version is {row.Version}.")
            : Result<ContentType>.Success(ToContentType(row, stored));
    }

    private Task<ContentTypeRow?> FindAsync(string slug, CancellationToken cancellationToken)
    {
        return context.ContentTypes
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.SiteId == SiteId && row.Slug == slug, cancellationToken);
    }

    // A reference to the type being written is allowed, so a category can have a parent category.
    private async Task<List<Error>> UnknownTargetsAsync(ContentType type, CancellationToken cancellationToken)
    {
        var references = type.Definition.References(fieldTypes).Where(reference => reference.Target != type.Slug).ToList();
        if (references.Count == 0)
        {
            return [];
        }

        var targets = references.Select(reference => reference.Target).Distinct().ToList();
        var existing = await context.ContentTypes
            .AsNoTracking()
            .Where(row => row.SiteId == type.SiteId && targets.Contains(row.Slug))
            .Select(row => row.Slug)
            .ToListAsync(cancellationToken);

        return references
            .Where(reference => !existing.Contains(reference.Target))
            .Select(reference => new Error(
                SchemaErrorCodes.ReferenceTargetUnknown,
                $"Field '{reference.Field}': there is no content type '{reference.Target}' in this site to reference."))
            .ToList();
    }

    private static async Task<Result<ContentType>> CommitAsync(
        IUnitOfWorkScope scope,
        ContentType type,
        Error conflict,
        CancellationToken cancellationToken)
    {
        try
        {
            await scope.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (DatabaseErrors.IsUniqueViolation(exception))
        {
            // The scope rolls back when the caller disposes it.
            return Result<ContentType>.Failure([conflict]);
        }

        return Result<ContentType>.Success(type);
    }

    private static ContentTypeVersionRow VersionRow(ContentType type)
    {
        return new ContentTypeVersionRow
        {
            Id = Guid.CreateVersion7(),
            ContentTypeId = type.Id,
            Version = type.Version,
            Definition = type.Definition.ToJson(),
        };
    }

    private static ContentType ToContentType(ContentTypeRow row, ContentTypeVersionRow version)
    {
        var definition = ContentTypeDefinition.Parse(version.Definition);
        if (!definition.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Version {version.Version} of content type '{row.Slug}' is stored in a form that cannot be read: "
                + string.Join("; ", definition.Errors.Select(error => error.Message)));
        }

        return ContentType.Restore(row.Id, row.SiteId, row.Name, row.Slug, version.Version, definition.Value);
    }

    private static Result<ContentType> NotFound(string slug)
    {
        return Result<ContentType>.Failure(SchemaErrorCodes.ContentTypeNotFound, $"There is no content type '{slug}' in this site.");
    }

    private static Error SlugTaken(string slug)
    {
        return new Error(SchemaErrorCodes.SlugTaken, $"The slug '{slug}' is already used by another content type in this site.");
    }

    private static Error VersionConflict(string slug, int baseVersion)
    {
        return new Error(
            SchemaErrorCodes.VersionConflict,
            $"Version {baseVersion} is not the current version of the content type '{slug}'; read it again and revise the current version.");
    }
}
