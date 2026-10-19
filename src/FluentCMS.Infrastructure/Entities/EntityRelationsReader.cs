using FluentCMS.Core.Agents;
using FluentCMS.Core.Content;
using FluentCMS.Core.Entities;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Infrastructure.Entities;

// Reads the reference index in one query per direction, whatever the number of relations (ADR-0005). Only the versions an entity
// points at count, as for where used, and deleted entities are left out on both ends. An agent reads relations of an entity of a type
// it may read, and sees only related entities of types it may read (MCP-05).
internal sealed class EntityRelationsReader(FluentCmsDbContext context, IAgentPermissions permissions) : IEntityRelations
{
    // Until multi-site (MST-01), every entry belongs to the one site (SCH-07).
    private static readonly Guid SiteId = Site.Default.Id;

    public async Task<Result<EntityRelations>> GetAsync(Guid entityId, CancellationToken cancellationToken = default)
    {
        var entity = await (
            from entry in context.Entries.AsNoTracking()
            join type in context.ContentTypes.AsNoTracking() on entry.ContentTypeId equals type.Id
            where entry.Id == entityId && entry.SiteId == SiteId && entry.DeletedAt == null
            select new { entry.ContentTypeId, type.Slug, entry.LatestVersion, entry.PublishedVersion })
            .SingleOrDefaultAsync(cancellationToken);
        if (entity is null)
        {
            return Result<EntityRelations>.Failure(ContentErrorCodes.EntryNotFound, $"There is no entry {entityId} in this site.");
        }

        var entityTypes = await EntityTypes.LoadAsync(context, cancellationToken);
        if (!entityTypes.ContainsKey(entity.ContentTypeId))
        {
            return Result<EntityRelations>.Failure(
                ContentErrorCodes.EntryNotEntity,
                $"Entry {entityId} is a '{entity.Slug}', which is not an entity type, so it has no relations; ask where it is used instead.");
        }

        var allowed = await permissions.CheckContentAsync(entity.Slug, AgentOperation.Read, cancellationToken: cancellationToken);
        if (!allowed.IsSuccess)
        {
            return Result<EntityRelations>.Failure(allowed.Errors);
        }

        var readable = await permissions.ReadableTypesAsync(cancellationToken);
        bool Related(Guid typeId) => entityTypes.TryGetValue(typeId, out var slug) && readable(slug);

        var outgoing = await (
            from reference in context.EntryReferences.AsNoTracking()
            join target in context.Entries.AsNoTracking() on reference.TargetId equals target.Id
            where reference.EntryId == entityId
                && reference.TargetKind == ReferenceKind.Entry
                && (reference.Version == entity.LatestVersion || reference.Version == entity.PublishedVersion)
                && target.SiteId == SiteId
                && target.DeletedAt == null
            select new { reference.Version, reference.Path, reference.TargetId, reference.TargetField, target.ContentTypeId })
            .ToListAsync(cancellationToken);

        var incoming = await (
            from reference in context.EntryReferences.AsNoTracking()
            join source in context.Entries.AsNoTracking() on reference.EntryId equals source.Id
            where reference.TargetId == entityId
                && reference.TargetKind == ReferenceKind.Entry
                && source.SiteId == SiteId
                && source.DeletedAt == null
                && (reference.Version == source.LatestVersion || reference.Version == source.PublishedVersion)
            select new { source.Id, source.ContentTypeId, reference.Version, source.LatestVersion, source.PublishedVersion, reference.Path, reference.TargetField })
            .ToListAsync(cancellationToken);

        var relationsFrom = outgoing
            .Where(row => Related(row.ContentTypeId))
            .GroupBy(row => (Field: Field(row.Path), row.TargetId, row.TargetField, row.ContentTypeId))
            .Select(group => new EntityRelation(
                entityId,
                entity.Slug,
                group.Key.Field,
                group.Key.TargetId,
                entityTypes[group.Key.ContentTypeId],
                group.Key.TargetField,
                group.Any(row => row.Version == entity.LatestVersion),
                group.Any(row => row.Version == entity.PublishedVersion)))
            .OrderBy(relation => relation.Field, StringComparer.Ordinal)
            .ThenBy(relation => relation.ToType, StringComparer.Ordinal)
            .ThenBy(relation => relation.ToId)
            .ToList();

        var relationsTo = incoming
            .Where(row => Related(row.ContentTypeId))
            .GroupBy(row => (row.Id, Field: Field(row.Path), row.TargetField, row.ContentTypeId))
            .Select(group => new EntityRelation(
                group.Key.Id,
                entityTypes[group.Key.ContentTypeId],
                group.Key.Field,
                entityId,
                entity.Slug,
                group.Key.TargetField,
                group.Any(row => row.Version == row.LatestVersion),
                group.Any(row => row.Version == row.PublishedVersion)))
            .OrderBy(relation => relation.FromType, StringComparer.Ordinal)
            .ThenBy(relation => relation.Field, StringComparer.Ordinal)
            .ThenBy(relation => relation.FromId)
            .ToList();

        return Result<EntityRelations>.Success(new EntityRelations(entityId, entity.Slug, relationsFrom, relationsTo));
    }

    // The index stores pointers from the document root, so the field is the first segment, such as category in /category/value.
    private static string Field(string path)
    {
        return path.Split('/')[1];
    }
}
