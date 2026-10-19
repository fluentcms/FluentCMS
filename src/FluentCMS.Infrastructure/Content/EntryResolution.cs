using System.Text.Json;
using FluentCMS.Core.Content;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Infrastructure.Content;

// Resolves stored versions into views (CNT-04) in a fixed number of queries however many there are, so a page of the delivery API
// costs no more round trips than one entry. The references come from the reference index, never from the documents (ADR-0005).
internal static class EntryResolution
{
    // Until multi-site (MST-01), every entry belongs to the one site (SCH-07).
    private static readonly Guid SiteId = Site.Default.Id;

    // In the order given; every version must be stored. EntityVersions replaces an entity's published version with the one a change
    // set proposes, or with none where it unpublishes the entity, so a preview resolves as the set would publish (API-04).
    public static async Task<IReadOnlyList<ResolvedEntry>> ResolveAsync(
        FluentCmsDbContext context,
        IReadOnlyList<VersionToResolve> versions,
        IReadOnlyDictionary<Guid, int?> entityVersions,
        CancellationToken cancellationToken)
    {
        if (versions.Count == 0)
        {
            return [];
        }

        var ids = versions.Select(version => version.EntryId).Distinct().ToList();
        var numbers = versions.Select(version => version.Version).Distinct().ToList();
        var wanted = versions.Select(version => (version.EntryId, version.Version)).ToHashSet();

        // Every pair of a wanted entry and a wanted number is fetched, and the pairs not asked for are dropped here.
        var stored = (await context.EntryVersions
            .AsNoTracking()
            .Where(row => ids.Contains(row.EntryId) && numbers.Contains(row.Version))
            .Select(row => new { row.EntryId, row.Version, row.Fields })
            .ToListAsync(cancellationToken))
            .Where(row => wanted.Contains((row.EntryId, row.Version)))
            .ToDictionary(row => (row.EntryId, row.Version), row => row.Fields);

        var references = (await context.EntryReferences
            .AsNoTracking()
            .Where(row => ids.Contains(row.EntryId)
                && numbers.Contains(row.Version)
                && row.TargetKind == ReferenceKind.Entry
                && row.TargetField != null)
            .Select(row => new { row.EntryId, row.Version, row.Path, row.TargetId, Field = row.TargetField! })
            .ToListAsync(cancellationToken))
            .Where(row => wanted.Contains((row.EntryId, row.Version)))
            .ToLookup(row => (row.EntryId, row.Version));

        var published = await PublishedFieldsAsync(
            context,
            references.SelectMany(group => group).Select(row => row.TargetId).Distinct().ToList(),
            entityVersions,
            cancellationToken);

        return versions.Select(version =>
        {
            var fields = stored.GetValueOrDefault((version.EntryId, version.Version))
                ?? throw new InvalidOperationException($"Entry {version.EntryId} has no stored version {version.Version}.");
            var values = new Dictionary<string, JsonElement?>(StringComparer.Ordinal);
            var unresolved = new List<UnresolvedReference>();

            foreach (var reference in references[(version.EntryId, version.Version)].OrderBy(reference => reference.Path, StringComparer.Ordinal))
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
            return new ResolvedEntry(version.EntryId, version.ContentType, version.Version, ResolvedFields.Replace(document.RootElement, values), unresolved);
        }).ToList();
    }

    // Each entity's published fields: none when it is deleted or has no published version.
    private static async Task<Dictionary<Guid, (bool Deleted, JsonElement? Fields)>> PublishedFieldsAsync(
        FluentCmsDbContext context,
        List<Guid> ids,
        IReadOnlyDictionary<Guid, int?> entityVersions,
        CancellationToken cancellationToken)
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

        var fields = rows.ToDictionary(row => row.Id, row => (row.Deleted, Fields: (string?)row.Fields));

        var proposed = ids.Where(entityVersions.ContainsKey).ToList();
        if (proposed.Count > 0)
        {
            var numbers = proposed.Select(id => entityVersions[id]).OfType<int>().Distinct().ToList();
            var stored = (await context.EntryVersions
                .AsNoTracking()
                .Where(version => proposed.Contains(version.EntryId) && numbers.Contains(version.Version))
                .Select(version => new { version.EntryId, version.Version, version.Fields })
                .ToListAsync(cancellationToken))
                .Where(version => entityVersions[version.EntryId] == version.Version)
                .ToDictionary(version => version.EntryId, version => version.Fields);

            foreach (var id in proposed.Where(fields.ContainsKey))
            {
                fields[id] = (fields[id].Deleted, stored.GetValueOrDefault(id));
            }
        }

        return fields.ToDictionary(
            pair => pair.Key,
            pair => (pair.Value.Deleted, pair.Value.Deleted || pair.Value.Fields is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(pair.Value.Fields)));
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

internal sealed record VersionToResolve(Guid EntryId, string ContentType, int Version);
