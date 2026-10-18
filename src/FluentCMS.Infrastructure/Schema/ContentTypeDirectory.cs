using FluentCMS.Core.Agents;
using FluentCMS.Core.Audit;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Sites;
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
    IAgentPermissions permissions) : IContentTypeDirectory
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
            .Select(type => new ContentTypeSummary(type.Id, type.Name, type.Slug, type.Version))
            .ToListAsync(cancellationToken);

        return types.Where(type => readable(type.Slug)).ToList();
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

        if (row.Version != baseVersion)
        {
            return Result<ContentType>.Failure([VersionConflict(slug, baseVersion)]);
        }

        var current = await context.ContentTypeVersions
            .AsNoTracking()
            .SingleAsync(version => version.ContentTypeId == row.Id && version.Version == row.Version, cancellationToken);

        var previous = ToContentType(row, current);
        var revised = previous.Revise(definition, fieldTypes);
        if (!revised.IsSuccess)
        {
            return revised;
        }

        var type = revised.Value;
        var errors = await ReferenceErrorsAsync(type, cancellationToken);
        errors.AddRange(await RemovedFieldsInUseAsync(type, previous.Definition, cancellationToken));
        if (errors.Count > 0)
        {
            return Result<ContentType>.Failure(errors);
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

    // A reference to the type being written is checked against the definition being written, so a category can have a parent category.
    private async Task<List<Error>> ReferenceErrorsAsync(ContentType type, CancellationToken cancellationToken)
    {
        var references = type.Definition.References(fieldTypes);
        if (references.Count == 0)
        {
            return [];
        }

        var others = references.Select(reference => reference.Target.Type).Where(slug => slug != type.Slug).Distinct().ToList();
        var targets = others.Count == 0 ? [] : await CurrentDefinitionsAsync(others, cancellationToken);
        targets[type.Slug] = type.Definition;

        var errors = new List<Error>();
        foreach (var (field, target) in references)
        {
            if (!targets.TryGetValue(target.Type, out var definition))
            {
                errors.Add(new Error(
                    SchemaErrorCodes.ReferenceTargetUnknown,
                    $"Field '{field}': there is no content type '{target.Type}' in this site to reference."));
            }
            else if (target.Field is null)
            {
                continue;
            }
            else if (!definition.IsEntity)
            {
                // Only facts are referenced field by field (SCH-04); anything else is referenced as a whole entry.
                errors.Add(new Error(
                    SchemaErrorCodes.ReferenceTargetNotEntity,
                    $"Field '{field}': '{target.Type}' is not an entity type, so its fields cannot be referenced; reference its entries instead."));
            }
            else if (!definition.HasField(target.Field))
            {
                errors.Add(new Error(
                    SchemaErrorCodes.ReferenceFieldUnknown,
                    $"Field '{field}': the entity type '{target.Type}' has no field '{target.Field}'."));
            }
        }

        return errors;
    }

    // A field another type points at cannot go, or that reference would dangle (SCH-04).
    private async Task<List<Error>> RemovedFieldsInUseAsync(ContentType type, ContentTypeDefinition previous, CancellationToken cancellationToken)
    {
        var removed = previous.Fields.Select(field => field.Name).Where(name => !type.Definition.HasField(name)).ToHashSet();
        if (removed.Count == 0)
        {
            return [];
        }

        var definitions = await CurrentDefinitionsAsync(slugs: null, cancellationToken);

        return definitions
            .Where(other => other.Key != type.Slug)
            .OrderBy(other => other.Key, StringComparer.Ordinal)
            .SelectMany(other => other.Value.References(fieldTypes).Select(reference => (Type: other.Key, Reference: reference)))
            .Where(use => use.Reference.Target.Type == type.Slug && use.Reference.Target.Field is { } field && removed.Contains(field))
            .Select(use => new Error(
                SchemaErrorCodes.FieldReferenced,
                $"Field '{use.Reference.Target.Field}' is referenced by the field '{use.Reference.Field}' of '{use.Type}'; change that type first."))
            .ToList();
    }

    // The current definition of every type in the site, or of the given slugs only.
    private async Task<Dictionary<string, ContentTypeDefinition>> CurrentDefinitionsAsync(
        IReadOnlyCollection<string>? slugs,
        CancellationToken cancellationToken)
    {
        var query =
            from type in context.ContentTypes.AsNoTracking()
            join version in context.ContentTypeVersions.AsNoTracking()
                on new { ContentTypeId = type.Id, type.Version } equals new { version.ContentTypeId, version.Version }
            where type.SiteId == SiteId
            select new { type.Slug, version.Version, version.Definition };

        if (slugs is not null)
        {
            query = query.Where(row => slugs.Contains(row.Slug));
        }

        var rows = await query.ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.Slug, row => ReadDefinition(row.Slug, row.Version, row.Definition));
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

    private static Error VersionConflict(string slug, int baseVersion)
    {
        return new Error(
            SchemaErrorCodes.VersionConflict,
            $"Version {baseVersion} is not the current version of the content type '{slug}'; read it again and revise the current version.");
    }
}
