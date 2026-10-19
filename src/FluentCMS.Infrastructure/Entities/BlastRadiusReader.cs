using System.Text.Json;
using FluentCMS.Core.Agents;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Entities;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Infrastructure.Entities;

// A fixed number of queries whatever the number of pages: the set's entity operations, the versions they name and the published
// references to the changed entities, so hundreds of pages stay well inside PERF-02's budget. An agent sees only entries of types it
// may read (MCP-05).
internal sealed class BlastRadiusReader(FluentCmsDbContext context, IAgentPermissions permissions) : IBlastRadius
{
    // Until multi-site (MST-01), every set and entry belongs to the one site (SCH-07).
    private static readonly Guid SiteId = Site.Default.Id;

    public async Task<Result<BlastRadius>> ForChangeSetAsync(Guid changeSetId, CancellationToken cancellationToken = default)
    {
        if (!await context.ChangeSets.AnyAsync(row => row.Id == changeSetId && row.SiteId == SiteId, cancellationToken))
        {
            return Result<BlastRadius>.Failure(ChangeSetErrorCodes.ChangeSetNotFound, $"There is no change set {changeSetId} in this site.");
        }

        var operations = await context.ChangeSetOperations
            .AsNoTracking()
            .Where(row => row.ChangeSetId == changeSetId && row.ItemKind == ItemKind.Entity)
            .Select(row => new { row.ItemId, row.BaseVersion, row.ProposedVersion })
            .ToListAsync(cancellationToken);

        var changes = await ChangedValuesAsync(
            [.. operations.Select(operation => (operation.ItemId, operation.BaseVersion, operation.ProposedVersion))],
            cancellationToken);
        if (changes.Count == 0)
        {
            return Result<BlastRadius>.Success(new BlastRadius(changeSetId, []));
        }

        var entities = changes.Keys.Select(key => key.EntityId).Distinct().ToList();
        var references = await (
            from reference in context.EntryReferences.AsNoTracking()
            join entry in context.Entries.AsNoTracking() on reference.EntryId equals entry.Id
            join type in context.ContentTypes.AsNoTracking() on entry.ContentTypeId equals type.Id
            where reference.TargetKind == ReferenceKind.Entry
                && reference.TargetField != null
                && entities.Contains(reference.TargetId)
                && entry.SiteId == SiteId
                && entry.DeletedAt == null
                && reference.Version == entry.PublishedVersion
            select new { entry.Id, type.Slug, reference.Version, reference.Path, reference.TargetId, Field = reference.TargetField! })
            .ToListAsync(cancellationToken);

        var readable = await permissions.ReadableTypesAsync(cancellationToken);

        var affected = references
            .Where(reference => readable(reference.Slug))
            .Select(reference => (Reference: reference, Change: changes.GetValueOrDefault((reference.TargetId, reference.Field))))
            .Where(item => item.Change is not null)
            .GroupBy(item => (item.Reference.Id, item.Reference.Slug, item.Reference.Version))
            .Select(group =>
            {
                var paths = group.OrderBy(item => item.Reference.Path, StringComparer.Ordinal).ToList();
                var before = paths
                    .Where(item => item.Change!.Before is not null)
                    .Select(item => new KeyValuePair<string, JsonElement>(item.Reference.Path, item.Change!.Before!.Value));
                var after = paths
                    .Where(item => item.Change!.After is not null)
                    .Select(item => new KeyValuePair<string, JsonElement>(item.Reference.Path, item.Change!.After!.Value));

                return new AffectedEntry(group.Key.Id, group.Key.Slug, group.Key.Version, ValueDiff.Compare(before, after));
            })
            .OrderBy(entry => entry.ContentType, StringComparer.Ordinal)
            .ThenBy(entry => entry.EntryId)
            .ToList();

        return Result<BlastRadius>.Success(new BlastRadius(changeSetId, affected));
    }

    // The fact values each entity operation changes, by entity and field: the base version's value and the proposed one's, absent
    // where the version or the field is. A field whose value stays is no change, whatever happened to its metadata.
    private async Task<Dictionary<(Guid EntityId, string Field), Change>> ChangedValuesAsync(
        List<(Guid EntityId, int? BaseVersion, int? ProposedVersion)> operations,
        CancellationToken cancellationToken)
    {
        if (operations.Count == 0)
        {
            return [];
        }

        var ids = operations.Select(operation => operation.EntityId).Distinct().ToList();
        var numbers = operations.SelectMany(operation => new[] { operation.BaseVersion, operation.ProposedVersion }).OfType<int>().Distinct().ToList();
        var rows = await context.EntryVersions
            .AsNoTracking()
            .Where(row => ids.Contains(row.EntryId) && numbers.Contains(row.Version))
            .Select(row => new { row.EntryId, row.Version, row.Fields })
            .ToListAsync(cancellationToken);
        var documents = rows.ToDictionary(row => (row.EntryId, row.Version), row => JsonSerializer.Deserialize<JsonElement>(row.Fields));

        var changes = new Dictionary<(Guid EntityId, string Field), Change>();
        foreach (var (entity, baseVersion, proposedVersion) in operations)
        {
            var before = Values(documents, entity, baseVersion);
            var after = Values(documents, entity, proposedVersion);

            foreach (var field in before.Keys.Union(after.Keys))
            {
                var old = before.TryGetValue(field, out var earlier) ? earlier : (JsonElement?)null;
                var @new = after.TryGetValue(field, out var later) ? later : (JsonElement?)null;

                if (old is { } a && @new is { } b && JsonElement.DeepEquals(a, b))
                {
                    continue;
                }

                changes[(entity, field)] = new Change(old, @new);
            }
        }

        return changes;
    }

    // The value of each fact of one version; an operation is checked against its versions when it is added, so one is always stored.
    private static Dictionary<string, JsonElement> Values(Dictionary<(Guid, int), JsonElement> documents, Guid entity, int? version)
    {
        if (version is not { } number)
        {
            return [];
        }

        var document = documents.TryGetValue((entity, number), out var stored)
            ? stored
            : throw new InvalidOperationException($"Version {number} of entity {entity} is named by a change set but is not stored.");

        return document.EnumerateObject()
            .Where(property => property.Value.ValueKind == JsonValueKind.Object && property.Value.TryGetProperty(FactMetadata.Value, out _))
            .ToDictionary(property => property.Name, property => property.Value.GetProperty(FactMetadata.Value), StringComparer.Ordinal);
    }

    private sealed record Change(JsonElement? Before, JsonElement? After);
}
