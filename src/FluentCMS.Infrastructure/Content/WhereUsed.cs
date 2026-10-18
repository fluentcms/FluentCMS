using FluentCMS.Core.Content;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Infrastructure.Content;

// Reads the reference index, never the stored documents, so the answer is one indexed query on both providers (ADR-0005).
internal sealed class WhereUsed(FluentCmsDbContext context, IContentTypeDirectory contentTypes) : IWhereUsed
{
    // Until multi-site (MST-01), every entry belongs to the one site (SCH-07).
    private static readonly Guid SiteId = Site.Default.Id;

    public async Task<Result<IReadOnlyList<EntryUsage>>> ForEntryAsync(Guid entryId, CancellationToken cancellationToken = default)
    {
        if (await ContentTypeOfAsync(entryId, cancellationToken) is null)
        {
            return Result<IReadOnlyList<EntryUsage>>.Failure([EntryNotFound(entryId)]);
        }

        return Result<IReadOnlyList<EntryUsage>>.Success(await UsagesAsync(ReferenceKind.Entry, entryId, field: null, cancellationToken));
    }

    public async Task<Result<IReadOnlyList<EntryUsage>>> ForEntityFieldAsync(Guid entityId, string field, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(field);

        if (await ContentTypeOfAsync(entityId, cancellationToken) is not { } slug)
        {
            return Result<IReadOnlyList<EntryUsage>>.Failure([EntryNotFound(entityId)]);
        }

        // Types are never deleted, so the type of a stored entry is always there.
        var definition = (await contentTypes.GetAsync(slug, cancellationToken)).Value.Definition;
        if (!definition.IsEntity)
        {
            return Result<IReadOnlyList<EntryUsage>>.Failure(
                ContentErrorCodes.EntryNotEntity,
                $"Entry {entityId} is a '{slug}', which is not an entity type, so its fields are not referenced one by one; ask where the entry is used.");
        }

        if (!definition.HasField(field))
        {
            return Result<IReadOnlyList<EntryUsage>>.Failure(
                ContentErrorCodes.FieldUnknown,
                $"The entity type '{slug}' has no field '{field}'; its fields are {string.Join(", ", definition.Fields.Select(known => known.Name))}.");
        }

        return Result<IReadOnlyList<EntryUsage>>.Success(await UsagesAsync(ReferenceKind.Entry, entityId, field, cancellationToken));
    }

    public Task<IReadOnlyList<EntryUsage>> ForMediaAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        return UsagesAsync(ReferenceKind.Media, assetId, field: null, cancellationToken);
    }

    // Only the versions the entries point at count: an earlier version keeps its references in the index but is not a use.
    private async Task<IReadOnlyList<EntryUsage>> UsagesAsync(ReferenceKind kind, Guid targetId, string? field, CancellationToken cancellationToken)
    {
        var query =
            from reference in context.EntryReferences.AsNoTracking()
            join entry in context.Entries.AsNoTracking() on reference.EntryId equals entry.Id
            join type in context.ContentTypes.AsNoTracking() on entry.ContentTypeId equals type.Id
            where entry.SiteId == SiteId
                && reference.TargetKind == kind
                && reference.TargetId == targetId
                && (reference.Version == entry.LatestVersion || reference.Version == entry.PublishedVersion)
            select new { entry.Id, type.Slug, reference.Version, entry.LatestVersion, entry.PublishedVersion, reference.Path, reference.TargetField };

        if (field is not null)
        {
            query = query.Where(row => row.TargetField == field);
        }

        var rows = await query.ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => (row.Id, row.Version))
            .OrderBy(group => group.Key.Id)
            .ThenBy(group => group.Key.Version)
            .Select(group =>
            {
                var first = group.First();
                return new EntryUsage(
                    first.Id,
                    first.Slug,
                    first.Version,
                    first.Version == first.LatestVersion,
                    first.Version == first.PublishedVersion,
                    group.Select(row => row.Path).Order(StringComparer.Ordinal).ToList());
            })
            .ToList();
    }

    private Task<string?> ContentTypeOfAsync(Guid entryId, CancellationToken cancellationToken)
    {
        return (
            from entry in context.Entries.AsNoTracking()
            join type in context.ContentTypes.AsNoTracking() on entry.ContentTypeId equals type.Id
            where entry.Id == entryId && entry.SiteId == SiteId
            select type.Slug)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static Error EntryNotFound(Guid id)
    {
        return new Error(ContentErrorCodes.EntryNotFound, $"There is no entry {id} in this site.");
    }
}
