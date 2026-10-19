using FluentCMS.Core.Agents;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Content;
using FluentCMS.Core.Entities;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FluentCMS.Infrastructure.Entities;

// Reads the fact index, never the documents (ADR-0005). An agent reads the facts of an entity of a type it may read, and lists only
// facts of such types (MCP-05).
internal sealed class EntityFactsReader(
    FluentCmsDbContext context,
    IAgentPermissions permissions,
    TimeProvider clock,
    IOptions<EntityOptions> options) : IEntityFacts
{
    // Until multi-site (MST-01), every entry belongs to the one site (SCH-07).
    private static readonly Guid SiteId = Site.Default.Id;

    public async Task<Result<EntityFactSheet>> GetAsync(Guid entityId, int? version = null, CancellationToken cancellationToken = default)
    {
        var entity = await (
            from entry in context.Entries.AsNoTracking()
            join type in context.ContentTypes.AsNoTracking() on entry.ContentTypeId equals type.Id
            where entry.Id == entityId && entry.SiteId == SiteId && entry.DeletedAt == null
            select new { entry.ContentTypeId, type.Slug, entry.LatestVersion })
            .SingleOrDefaultAsync(cancellationToken);
        if (entity is null)
        {
            return Result<EntityFactSheet>.Failure(ContentErrorCodes.EntryNotFound, $"There is no entry {entityId} in this site.");
        }

        if (!(await EntityTypes.LoadAsync(context, cancellationToken)).ContainsKey(entity.ContentTypeId))
        {
            return Result<EntityFactSheet>.Failure(
                ContentErrorCodes.EntryNotEntity,
                $"Entry {entityId} is a '{entity.Slug}', which is not an entity type, so its fields are not facts.");
        }

        var allowed = await permissions.CheckContentAsync(entity.Slug, AgentOperation.Read, cancellationToken: cancellationToken);
        if (!allowed.IsSuccess)
        {
            return Result<EntityFactSheet>.Failure(allowed.Errors);
        }

        var number = version ?? entity.LatestVersion;
        if (!await context.EntryVersions.AnyAsync(row => row.EntryId == entityId && row.Version == number, cancellationToken))
        {
            return Result<EntityFactSheet>.Failure(
                ContentErrorCodes.VersionNotFound,
                $"Entry {entityId} has no version {number}; its latest version is {entity.LatestVersion}.");
        }

        var facts = await context.EntityFacts
            .AsNoTracking()
            .Where(row => row.EntryId == entityId && row.Version == number)
            .OrderBy(row => row.Position)
            .ToListAsync(cancellationToken);

        return Result<EntityFactSheet>.Success(new EntityFactSheet(entityId, entity.Slug, number, [.. facts.Select(ToFact)]));
    }

    public async Task<Result<IReadOnlyList<FactFlag>>> UnsourcedAsync(string? contentType = null, CancellationToken cancellationToken = default)
    {
        var entityTypes = await EntityTypes.LoadAsync(context, cancellationToken);
        var type = await EntityTypeAsync(contentType, entityTypes, cancellationToken);
        if (!type.IsSuccess)
        {
            return Result<IReadOnlyList<FactFlag>>.Failure(type.Errors);
        }

        var typeId = type.Value;
        var rows = await (
            from fact in context.EntityFacts.AsNoTracking()
            join entry in context.Entries.AsNoTracking() on fact.EntryId equals entry.Id
            where fact.Unsourced
                && entry.SiteId == SiteId
                && entry.DeletedAt == null
                && (typeId == null || entry.ContentTypeId == typeId)
                && (fact.Version == entry.LatestVersion || fact.Version == entry.PublishedVersion)
            select new FlaggedRow(entry.Id, entry.ContentTypeId, fact.Field, fact.Position, fact.Version, entry.LatestVersion, entry.PublishedVersion))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<FactFlag>>.Success(await FlagsAsync(rows, entityTypes, cancellationToken));
    }

    public async Task<Result<IReadOnlyList<FactFlag>>> UnsourcedInChangeSetAsync(Guid changeSetId, CancellationToken cancellationToken = default)
    {
        if (!await context.ChangeSets.AnyAsync(row => row.Id == changeSetId && row.SiteId == SiteId, cancellationToken))
        {
            return Result<IReadOnlyList<FactFlag>>.Failure(ChangeSetErrorCodes.ChangeSetNotFound, $"There is no change set {changeSetId} in this site.");
        }

        // A delete proposes no version, so it brings no facts.
        var rows = await (
            from operation in context.ChangeSetOperations.AsNoTracking()
            join fact in context.EntityFacts.AsNoTracking()
                on new { EntryId = operation.ItemId, Version = operation.ProposedVersion!.Value } equals new { fact.EntryId, fact.Version }
            join entry in context.Entries.AsNoTracking() on fact.EntryId equals entry.Id
            where operation.ChangeSetId == changeSetId
                && operation.ItemKind == ItemKind.Entity
                && operation.ProposedVersion != null
                && fact.Unsourced
                && entry.DeletedAt == null
            select new FlaggedRow(entry.Id, entry.ContentTypeId, fact.Field, fact.Position, fact.Version, entry.LatestVersion, entry.PublishedVersion))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<FactFlag>>.Success(await FlagsAsync(rows, await EntityTypes.LoadAsync(context, cancellationToken), cancellationToken));
    }

    public async Task<Result<IReadOnlyList<StaleFact>>> StaleAsync(string? contentType = null, CancellationToken cancellationToken = default)
    {
        var entityTypes = await EntityTypes.LoadAsync(context, cancellationToken);
        var type = await EntityTypeAsync(contentType, entityTypes, cancellationToken);
        if (!type.IsSuccess)
        {
            return Result<IReadOnlyList<StaleFact>>.Failure(type.Errors);
        }

        var typeId = type.Value;
        var verifyWithin = options.Value.VerifyWithin;
        var now = clock.GetUtcNow().UtcDateTime;
        var verifiedBefore = now - verifyWithin;

        // Both times are indexed UTC columns, so each provider filters them itself.
        var rows = await (
            from fact in context.EntityFacts.AsNoTracking()
            join entry in context.Entries.AsNoTracking() on fact.EntryId equals entry.Id
            where (fact.ValidUntil < now || fact.LastVerified < verifiedBefore)
                && entry.SiteId == SiteId
                && entry.DeletedAt == null
                && (typeId == null || entry.ContentTypeId == typeId)
                && (fact.Version == entry.LatestVersion || fact.Version == entry.PublishedVersion)
            select new
            {
                Row = new FlaggedRow(entry.Id, entry.ContentTypeId, fact.Field, fact.Position, fact.Version, entry.LatestVersion, entry.PublishedVersion),
                fact.ValidUntil,
                fact.LastVerified,
            })
            .ToListAsync(cancellationToken);

        var readable = await permissions.ReadableTypesAsync(cancellationToken);

        return Result<IReadOnlyList<StaleFact>>.Success(rows
            .Where(item => entityTypes.TryGetValue(item.Row.ContentTypeId, out var slug) && readable(slug))
            .Select(item =>
            {
                var expired = item.ValidUntil < now;
                return (item.Row, Fact: new StaleFact(
                    item.Row.EntityId,
                    entityTypes[item.Row.ContentTypeId],
                    item.Row.Field,
                    item.Row.Version,
                    item.Row.Version == item.Row.LatestVersion,
                    item.Row.Version == item.Row.PublishedVersion,
                    expired ? StaleReason.Expired : StaleReason.Unverified,
                    Utc(expired ? item.ValidUntil : item.LastVerified + verifyWithin)!.Value));
            })
            .OrderBy(item => item.Fact.StaleSince)
            .ThenBy(item => item.Fact.Type, StringComparer.Ordinal)
            .ThenBy(item => item.Fact.EntityId)
            .ThenBy(item => item.Fact.Version)
            .ThenBy(item => item.Row.Position)
            .Select(item => item.Fact)
            .ToList());
    }

    // The entity type a list is narrowed to, or null for every type the caller may read.
    private async Task<Result<Guid?>> EntityTypeAsync(string? contentType, Dictionary<Guid, string> entityTypes, CancellationToken cancellationToken)
    {
        if (contentType is null)
        {
            return Result<Guid?>.Success(null);
        }

        var allowed = await permissions.CheckContentAsync(contentType, AgentOperation.Read, cancellationToken: cancellationToken);
        if (!allowed.IsSuccess)
        {
            return Result<Guid?>.Failure(allowed.Errors);
        }

        var type = await context.ContentTypes
            .AsNoTracking()
            .Where(row => row.SiteId == SiteId && row.Slug == contentType)
            .Select(row => (Guid?)row.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (type is null)
        {
            return Result<Guid?>.Failure(SchemaErrorCodes.ContentTypeNotFound, $"There is no content type '{contentType}' in this site.");
        }

        if (!entityTypes.ContainsKey(type.Value))
        {
            return Result<Guid?>.Failure(
                EntityErrorCodes.TypeNotEntity,
                $"The content type '{contentType}' is not an entity type, so its fields are not facts.");
        }

        return Result<Guid?>.Success(type);
    }

    private async Task<IReadOnlyList<FactFlag>> FlagsAsync(List<FlaggedRow> rows, Dictionary<Guid, string> entityTypes, CancellationToken cancellationToken)
    {
        var readable = await permissions.ReadableTypesAsync(cancellationToken);

        return rows
            .Where(row => entityTypes.TryGetValue(row.ContentTypeId, out var slug) && readable(slug))
            .Select(row => (Row: row, Flag: new FactFlag(
                row.EntityId,
                entityTypes[row.ContentTypeId],
                row.Field,
                row.Version,
                row.Version == row.LatestVersion,
                row.Version == row.PublishedVersion)))
            .OrderBy(item => item.Flag.Type, StringComparer.Ordinal)
            .ThenBy(item => item.Flag.EntityId)
            .ThenBy(item => item.Flag.Version)
            .ThenBy(item => item.Row.Position)
            .Select(item => item.Flag)
            .ToList();
    }

    private static Fact ToFact(EntityFactRow row)
    {
        return new Fact(row.Field, row.SourceKind, row.SourceReference, row.Owner, Utc(row.ValidFrom), Utc(row.ValidUntil), Utc(row.LastVerified));
    }

    private static DateTimeOffset? Utc(DateTime? time)
    {
        return time is { } value ? new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)) : null;
    }

    private sealed record FlaggedRow(Guid EntityId, Guid ContentTypeId, string Field, int Position, int Version, int LatestVersion, int? PublishedVersion);
}
