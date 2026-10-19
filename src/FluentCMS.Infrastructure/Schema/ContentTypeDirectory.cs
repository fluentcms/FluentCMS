using FluentCMS.Core.Agents;
using FluentCMS.Core.Audit;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.ChangeSets;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Infrastructure.Schema;

// Reads never track rows, so a long-lived context (a Blazor circuit) always sees what is stored, not what it saw before.
// An agent reads and writes only the types its scope allows (MCP-05); the scope is checked before anything else.
internal sealed class ContentTypeDirectory(
    FluentCmsDbContext context,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    FieldTypeRegistry fieldTypes,
    IAgentPermissions permissions,
    ChangeSetDirectory changeSets) : IContentTypeDirectory
{
    private const string SubjectType = "content_type";

    // Until multi-site (MST-01), every type belongs to the one site (SCH-07).
    private static readonly Guid SiteId = Site.Default.Id;

    public async Task<IReadOnlyList<ContentTypeSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var readable = await permissions.ReadableTypesAsync(cancellationToken);
        var types = await context.ContentTypes
            .AsNoTracking()
            .Where(type => type.SiteId == SiteId)
            .OrderBy(type => type.Slug)
            .Select(type => new ContentTypeSummary(type.Id, type.Name, type.Slug, type.Version, type.PublishedVersion))
            .ToListAsync(cancellationToken);

        return types.Where(type => readable(type.Slug)).ToList();
    }

    public Task<Result<ContentType>> GetAsync(string slug, CancellationToken cancellationToken = default)
    {
        return ReadAsync(slug, row => row.Version, cancellationToken);
    }

    public Task<Result<ContentType>> GetPublishedAsync(string slug, CancellationToken cancellationToken = default)
    {
        return ReadAsync(slug, row => row.PublishedVersion, cancellationToken);
    }

    public Task<Result<ContentType>> GetVersionAsync(string slug, int version, CancellationToken cancellationToken = default)
    {
        return ReadAsync(slug, _ => version, cancellationToken);
    }

    public async Task<Result<ContentType>> CreateAsync(NewContentType contentType, ChangeSetTarget changeSet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contentType);
        ArgumentNullException.ThrowIfNull(changeSet);

        var allowed = await permissions.CheckDefinitionAsync(contentType.Slug, AgentOperation.Create, cancellationToken);
        if (!allowed.IsSuccess)
        {
            return Result<ContentType>.Failure(allowed.Errors);
        }

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

        errors.AddRange(await ReferenceErrorsAsync(type, cancellationToken));
        if (errors.Count > 0)
        {
            return Result<ContentType>.Failure(errors);
        }

        var pending = await changeSets.PrepareAsync(changeSet, Operation(OperationKind.Create, type.Id, null, type.Version), itemPublished: false, cancellationToken);
        if (!pending.IsSuccess)
        {
            return Result<ContentType>.Failure(pending.Errors);
        }

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var begun = await changeSets.BeginWriteAsync(pending.Value, cancellationToken);
        if (!begun.IsSuccess)
        {
            return Result<ContentType>.Failure(begun.Errors);
        }

        context.ContentTypes.Add(new ContentTypeRow
        {
            Id = type.Id,
            SiteId = type.SiteId,
            Name = type.Name,
            Slug = type.Slug,
            Version = type.Version,
        });
        context.ContentTypeVersions.Add(VersionRow(type));
        await auditLog.AppendAsync(
            new NewAuditEvent("content_type.created") { SubjectType = SubjectType, SubjectId = type.Id, ChangeSetId = pending.Value.ChangeSet.Id },
            cancellationToken);
        await changeSets.RecordAsync(pending.Value, null, cancellationToken);

        return await CommitAsync(scope, type, CommitConflict(changeSet, pending.Value, SlugTaken(type.Slug)), cancellationToken);
    }

    public async Task<Result<ContentType>> ReviseAsync(
        string slug,
        int baseVersion,
        ContentTypeDefinition definition,
        ChangeSetTarget changeSet,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(slug);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(changeSet);

        var allowed = await permissions.CheckDefinitionAsync(slug, AgentOperation.Update, cancellationToken);
        if (!allowed.IsSuccess)
        {
            return Result<ContentType>.Failure(allowed.Errors);
        }

        var row = await FindAsync(slug, cancellationToken);
        if (row is null)
        {
            return NotFound(slug);
        }

        // The published version is a base too, so a revision can leave an unpublished draft behind, such as a rejected one. A set that
        // already holds the type is revised from the version it proposes, however many drafts other sets have written since.
        var own = await changeSets.ProposedVersionAsync(changeSet, ItemKind.Schema, row.Id, cancellationToken);
        if (own is { } proposed ? baseVersion != proposed : baseVersion != row.Version && baseVersion != row.PublishedVersion)
        {
            return Result<ContentType>.Failure([VersionConflict(row, baseVersion, own)]);
        }

        // A draft is only worth building on while a set that can still publish proposes it; a revision of a rejected or reverted
        // draft could never publish over the version that is live.
        if (row.PublishedVersion is { } published
            && baseVersion != published
            && !await changeSets.ProposesAsync(ItemKind.Schema, row.Id, baseVersion, cancellationToken))
        {
            return Result<ContentType>.Failure(
                SchemaErrorCodes.VersionConflict,
                $"Version {baseVersion} of the content type '{slug}' is a draft that no open change set proposes any more, so it will never "
                + $"be published; revise the published version {published} instead.");
        }

        // A type with nothing live is made live again by a create, so the revision needs the scope to create it (MCP-05).
        if (row.PublishedVersion is null)
        {
            var creatable = await permissions.CheckDefinitionAsync(slug, AgentOperation.Create, cancellationToken);
            if (!creatable.IsSuccess)
            {
                return Result<ContentType>.Failure(creatable.Errors);
            }
        }

        var based = await context.ContentTypeVersions
            .AsNoTracking()
            .SingleAsync(version => version.ContentTypeId == row.Id && version.Version == baseVersion, cancellationToken);

        // Numbered after the latest version, whichever version the revision changes.
        var previous = ContentType.Restore(row.Id, row.SiteId, row.Name, row.Slug, row.Version, ReadDefinition(row.Slug, based.Version, based.Definition));
        var revised = previous.Revise(definition, fieldTypes);
        if (!revised.IsSuccess)
        {
            return revised;
        }

        // A revision may set drafts aside, such as the latest one when it is based on the published version. An open set may still
        // publish them and other drafts may build on them, so it must not take away what they give: their fields and their entity
        // flag (SCH-03, SCH-04).
        var live = (await LiveDefinitionsAsync([slug], cancellationToken)).GetValueOrDefault(slug) ?? [];
        var errors = new List<Error>();
        if (!definition.IsEntity && live.FirstOrDefault(candidate => !candidate.Published && candidate.Definition.IsEntity) is { } entity)
        {
            errors.Add(new Error(
                SchemaErrorCodes.EntityFlagRemoved,
                $"Version {entity.Version} of the content type '{slug}', which an open change set may still publish, makes it an entity "
                + "type, so a revision must keep \"entity\": true."));
        }

        var type = revised.Value;
        errors.AddRange(await ReferenceErrorsAsync(type, cancellationToken));
        errors.AddRange(await RemovedFieldsInUseAsync(type, [previous.Definition, .. live.Select(candidate => candidate.Definition)], cancellationToken));
        if (errors.Count > 0)
        {
            return Result<ContentType>.Failure(errors);
        }

        var pending = await changeSets.PrepareAsync(
            changeSet,
            Operation(OperationKind.Update, type.Id, baseVersion, type.Version),
            itemPublished: row.PublishedVersion is not null,
            cancellationToken);
        if (!pending.IsSuccess)
        {
            return Result<ContentType>.Failure(pending.Errors);
        }

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var begun = await changeSets.BeginWriteAsync(pending.Value, cancellationToken);
        if (!begun.IsSuccess)
        {
            return Result<ContentType>.Failure(begun.Errors);
        }

        // Moves the pointer only from the latest version this call read; a concurrent revision that committed first leaves nothing to move.
        var moved = await context.ContentTypes
            .Where(stored => stored.Id == row.Id && stored.Version == row.Version)
            .ExecuteUpdateAsync(setters => setters.SetProperty(stored => stored.Version, type.Version), cancellationToken);
        if (moved == 0)
        {
            return Result<ContentType>.Failure([RevisedMeanwhile(row)]);
        }

        context.ContentTypeVersions.Add(VersionRow(type));
        await auditLog.AppendAsync(
            new NewAuditEvent("content_type.revised")
            {
                SubjectType = SubjectType,
                SubjectId = type.Id,
                ChangeSetId = pending.Value.ChangeSet.Id,
                PreviousVersionId = based.Id,
            },
            cancellationToken);

        await changeSets.RecordAsync(pending.Value, based.Id, cancellationToken);

        return await CommitAsync(scope, type, CommitConflict(changeSet, pending.Value, RevisedMeanwhile(row)), cancellationToken);
    }

    private async Task<Result<ContentType>> ReadAsync(string slug, Func<ContentTypeRow, int?> version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(slug);

        var allowed = await permissions.CheckContentAsync(slug, AgentOperation.Read, cancellationToken: cancellationToken);
        if (!allowed.IsSuccess)
        {
            return Result<ContentType>.Failure(allowed.Errors);
        }

        var row = await FindAsync(slug, cancellationToken);
        if (row is null)
        {
            return NotFound(slug);
        }

        if (version(row) is not { } number)
        {
            return Result<ContentType>.Failure(
                SchemaErrorCodes.ContentTypeNotPublished,
                $"No change set has published the content type '{slug}' yet; its latest version is {row.Version}.");
        }

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

    // A reference to the type being written is checked against the definition being written, so a category can have a parent category.
    // Another type is checked against what it is heading to: every draft a set that can still publish proposes, or its published
    // version while there is none. A field only a rejected draft has is no target, and neither is one a pending draft removes.
    private async Task<List<Error>> ReferenceErrorsAsync(ContentType type, CancellationToken cancellationToken)
    {
        var references = type.Definition.References(fieldTypes);
        if (references.Count == 0)
        {
            return [];
        }

        var others = references.Select(reference => reference.Target.Type).Where(slug => slug != type.Slug).Distinct().ToList();
        var targets = others.Count == 0 ? [] : await LiveDefinitionsAsync(others, cancellationToken);

        var errors = new List<Error>();
        foreach (var (field, target) in references)
        {
            if (target.Type == type.Slug)
            {
                errors.AddRange(FieldTargetErrors(field, target, type.Definition));
            }
            else if (!targets.TryGetValue(target.Type, out var definitions))
            {
                errors.Add(new Error(
                    SchemaErrorCodes.ReferenceTargetUnknown,
                    $"Field '{field}': there is no content type '{target.Type}' in this site to reference."));
            }
            else
            {
                // A draft is named by its version when the published version may read differently.
                var drafts = definitions.Where(candidate => !candidate.Published).ToList();
                var published = drafts.Count < definitions.Count;
                errors.AddRange((drafts.Count > 0 ? drafts : definitions)
                    .Select(candidate => FieldTargetErrors(field, target, candidate.Definition, published && !candidate.Published ? candidate.Version : null))
                    .FirstOrDefault(found => found.Count > 0) ?? []);
            }
        }

        return errors;
    }

    // Only facts are referenced field by field (SCH-04); anything else is referenced as a whole entry.
    private static List<Error> FieldTargetErrors(string field, ReferenceTarget target, ContentTypeDefinition definition, int? draft = null)
    {
        if (target.Field is null)
        {
            return [];
        }

        string Name(string kind) => draft is { } version
            ? $"version {version} of {kind}'{target.Type}', which an open change set may still publish,"
            : $"{kind}'{target.Type}'";

        if (!definition.IsEntity)
        {
            return [new Error(
                SchemaErrorCodes.ReferenceTargetNotEntity,
                $"Field '{field}': {Name(string.Empty)} is not an entity type, so its fields cannot be referenced; reference its entries instead.")];
        }

        return definition.HasField(target.Field)
            ? []
            : [new Error(SchemaErrorCodes.ReferenceFieldUnknown, $"Field '{field}': {Name("the entity type ")} has no field '{target.Field}'.")];
    }

    // A field another type points at cannot go, or that reference would dangle (SCH-04), whether it points from that type's live
    // version or from a draft an open set may still publish.
    private async Task<List<Error>> RemovedFieldsInUseAsync(ContentType type, IEnumerable<ContentTypeDefinition> previous, CancellationToken cancellationToken)
    {
        var removed = previous.SelectMany(definition => definition.Fields).Select(field => field.Name).Where(name => !type.Definition.HasField(name)).ToHashSet();
        if (removed.Count == 0)
        {
            return [];
        }

        var definitions = await LiveDefinitionsAsync(slugs: null, cancellationToken);

        return definitions
            .Where(other => other.Key != type.Slug)
            .OrderBy(other => other.Key, StringComparer.Ordinal)
            .SelectMany(other => other.Value
                .SelectMany(candidate => candidate.Definition.References(fieldTypes))
                .DistinctBy(reference => (reference.Field, reference.Target.Type, reference.Target.Field))
                .Select(reference => (Type: other.Key, Reference: reference)))
            .Where(use => use.Reference.Target.Type == type.Slug && use.Reference.Target.Field is { } field && removed.Contains(field))
            .Select(use => new Error(
                SchemaErrorCodes.FieldReferenced,
                $"Field '{use.Reference.Target.Field}' is referenced by the field '{use.Reference.Field}' of '{use.Type}'; change that type first."))
            .ToList();
    }

    // What each type is live as or may become: its published definition, and every draft a set that can still publish proposes, in
    // version order. Every type in the site, or the given slugs only; a type with neither, such as one whose create was rejected, is
    // left out.
    private async Task<Dictionary<string, List<LiveDefinition>>> LiveDefinitionsAsync(
        IReadOnlyCollection<string>? slugs,
        CancellationToken cancellationToken)
    {
        var types = context.ContentTypes.AsNoTracking().Where(type => type.SiteId == SiteId);
        if (slugs is not null)
        {
            types = types.Where(type => slugs.Contains(type.Slug));
        }

        var proposed = changeSets.LiveOperations(ItemKind.Schema);
        var rows = await (
                from type in types
                join version in context.ContentTypeVersions.AsNoTracking() on type.Id equals version.ContentTypeId
                where version.Version == type.PublishedVersion
                    || proposed.Any(operation => operation.ItemId == type.Id && operation.ProposedVersion == version.Version)
                select new { type.Slug, type.PublishedVersion, version.Version, version.Definition })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.Slug, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(row => row.Version)
                    .Select(row => new LiveDefinition(row.Version, row.Version == row.PublishedVersion, ReadDefinition(row.Slug, row.Version, row.Definition)))
                    .ToList(),
                StringComparer.Ordinal);
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
        return ContentType.Restore(row.Id, row.SiteId, row.Name, row.Slug, version.Version, ReadDefinition(row.Slug, version.Version, version.Definition));
    }

    private static ContentTypeDefinition ReadDefinition(string slug, int version, string json)
    {
        var definition = ContentTypeDefinition.Parse(json);

        return definition.IsSuccess
            ? definition.Value
            : throw new InvalidOperationException(
                $"Version {version} of content type '{slug}' is stored in a form that cannot be read: "
                + string.Join("; ", definition.Errors.Select(error => error.Message)));
    }

    private static Result<ContentType> NotFound(string slug)
    {
        return Result<ContentType>.Failure(SchemaErrorCodes.ContentTypeNotFound, $"There is no content type '{slug}' in this site.");
    }

    private static Error SlugTaken(string slug)
    {
        return new Error(SchemaErrorCodes.SlugTaken, $"The slug '{slug}' is already used by another content type in this site.");
    }

    private static Error VersionConflict(ContentTypeRow row, int baseVersion, int? own)
    {
        return new Error(
            SchemaErrorCodes.VersionConflict,
            own is { } proposed
                ? $"The change set already proposes version {proposed} of the content type '{row.Slug}', so a later write of it in that set "
                    + $"revises version {proposed}, not {baseVersion}."
                : row.PublishedVersion is { } published && published != row.Version
                    ? $"Version {baseVersion} is neither the latest version {row.Version} of the content type '{row.Slug}' nor the published "
                        + $"version {published}; revise one of those."
                    : $"Version {baseVersion} is not the latest version {row.Version} of the content type '{row.Slug}'; revise that one.");
    }

    private static Error RevisedMeanwhile(ContentTypeRow row)
    {
        return new Error(
            SchemaErrorCodes.VersionConflict,
            $"Another call revised the content type '{row.Slug}' at the same time; read its latest version again and revise that one.");
    }

    private static ChangeOperation Operation(OperationKind kind, Guid contentTypeId, int? baseVersion, int proposedVersion)
    {
        return ChangeOperation.Create(kind, ItemKind.Schema, contentTypeId, baseVersion, proposedVersion).Value;
    }

    // A unique index at commit is this write's own conflict or, when the write opened its session's set, possibly a concurrent
    // first write of the same session.
    private static Error CommitConflict(ChangeSetTarget target, ChangeSetDirectory.PendingOperation pending, Error own)
    {
        return pending.Revision is null && target.SessionId is { } session
            ? new Error(
                ChangeSetErrorCodes.Conflict,
                $"Another call wrote this content type or opened a change set for session '{session}' at the same time; read it again and retry.")
            : own;
    }

    private sealed record LiveDefinition(int Version, bool Published, ContentTypeDefinition Definition);
}
