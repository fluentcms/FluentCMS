using System.Text.Json;
using FluentCMS.Core.Content;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Infrastructure.Content;

internal sealed class ResolvedEntries(FluentCmsDbContext context) : IResolvedEntries
{
    // Until multi-site (MST-01), every entry belongs to the one site (SCH-07).
    private static readonly Guid SiteId = Site.Default.Id;

    public async Task<Result<ResolvedEntry>> ResolveAsync(Guid entryId, int? version = null, CancellationToken cancellationToken = default)
    {
        var entry = await (
            from row in context.Entries.AsNoTracking()
            join type in context.ContentTypes.AsNoTracking() on row.ContentTypeId equals type.Id
            where row.Id == entryId && row.SiteId == SiteId && row.DeletedAt == null
            select new { row.LatestVersion, type.Slug })
            .SingleOrDefaultAsync(cancellationToken);
        if (entry is null)
        {
            return Result<ResolvedEntry>.Failure(ContentErrorCodes.EntryNotFound, $"There is no entry {entryId} in this site.");
        }

        var number = version ?? entry.LatestVersion;
        var fields = await context.EntryVersions
            .AsNoTracking()
            .Where(row => row.EntryId == entryId && row.Version == number)
            .Select(row => row.Fields)
            .SingleOrDefaultAsync(cancellationToken);
        if (fields is null)
        {
            return Result<ResolvedEntry>.Failure(
                ContentErrorCodes.VersionNotFound,
                $"Entry {entryId} has no version {number}; its latest version is {entry.LatestVersion}.");
        }

        var references = await context.EntryReferences
            .AsNoTracking()
            .Where(row => row.EntryId == entryId && row.Version == number && row.TargetKind == ReferenceKind.Entry && row.TargetField != null)
            .Select(row => new { row.Path, row.TargetId, Field = row.TargetField! })
            .ToListAsync(cancellationToken);

        var published = await PublishedFieldsAsync(references.Select(reference => reference.TargetId).Distinct().ToList(), cancellationToken);
        var values = new Dictionary<string, JsonElement?>(StringComparer.Ordinal);
        var unresolved = new List<UnresolvedReference>();

        foreach (var reference in references.OrderBy(reference => reference.Path, StringComparer.Ordinal))
        {
            var (deleted, entity) = published.GetValueOrDefault(reference.TargetId);
            var value = entity is { } fieldsOfEntity ? FactValue(fieldsOfEntity, reference.Field) : null;
            values[reference.Path] = value;

            if (value is null)
            {
                var reason = deleted ? UnresolvedReason.Deleted : entity is null ? UnresolvedReason.NotPublished : UnresolvedReason.NoValue;
                unresolved.Add(new UnresolvedReference(reference.Path, reference.TargetId, reference.Field, reason));
            }
        }

        using var document = JsonDocument.Parse(fields);
        return Result<ResolvedEntry>.Success(
            new ResolvedEntry(entryId, entry.Slug, number, ResolvedFields.Replace(document.RootElement, values), unresolved));
    }

    // Each entity's published fields, read in one query: none when it is deleted or has no published version.
    private async Task<Dictionary<Guid, (bool Deleted, JsonElement? Fields)>> PublishedFieldsAsync(List<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var rows = await (
            from entity in context.Entries.AsNoTracking()
            where ids.Contains(entity.Id) && entity.SiteId == SiteId
            from version in context.EntryVersions.AsNoTracking()
                .Where(version => version.EntryId == entity.Id && version.Version == entity.PublishedVersion)
                .DefaultIfEmpty()
            select new { entity.Id, Deleted = entity.DeletedAt != null, Fields = version == null ? null : version.Fields })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            row => row.Id,
            row => (row.Deleted, row.Deleted || row.Fields is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(row.Fields)));
    }

    // An entity stores each field as a fact; the view shows the fact's value (SCH-03).
    private static JsonElement? FactValue(JsonElement entity, string field)
    {
        return entity.TryGetProperty(field, out var fact)
            && fact.ValueKind == JsonValueKind.Object
            && fact.TryGetProperty(FactMetadata.Value, out var value)
            && value.ValueKind != JsonValueKind.Null
                ? value
                : null;
    }
}
