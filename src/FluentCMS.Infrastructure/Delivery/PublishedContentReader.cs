using System.Text.Json;
using FluentCMS.Core.Agents;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Content;
using FluentCMS.Core.Delivery;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Content;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Infrastructure.Delivery;

// An agent that reads through here keeps to its scope (MCP-05); people and anonymous readers see every published entry.
internal sealed class PublishedContentReader(FluentCmsDbContext context, IAgentPermissions permissions, TimeProvider clock)
    : IPublishedContent, IPreviewContent
{
    // Until multi-site (MST-01), every entry belongs to the one site (SCH-07).
    private static readonly Guid SiteId = Site.Default.Id;

    private static readonly Dictionary<Guid, int?> NoProposals = [];

    public async Task<Result<PublishedPage>> ListAsync(PublishedQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Limit is < 1 or > PublishedQuery.MaxLimit)
        {
            return Result<PublishedPage>.Failure(
                DeliveryErrorCodes.LimitInvalid,
                $"The limit is {query.Limit}; it must be from 1 to {PublishedQuery.MaxLimit}.");
        }

        var allowed = await permissions.CheckContentAsync(query.ContentType, AgentOperation.Read, cancellationToken: cancellationToken);
        if (!allowed.IsSuccess)
        {
            return Result<PublishedPage>.Failure(allowed.Errors);
        }

        if ((await TypesAsync(query.ContentType, null, cancellationToken)).Values.SingleOrDefault() is not { } type)
        {
            return Result<PublishedPage>.Failure(SchemaErrorCodes.ContentTypeNotFound, $"There is no content type '{query.ContentType}' in this site.");
        }

        var entries = context.Entries
            .AsNoTracking()
            .Where(entry => entry.SiteId == SiteId && entry.ContentTypeId == type.Id && entry.PublishedVersion != null && entry.DeletedAt == null);

        // Ids are version 7, so they sort in the order of creation and a page boundary does not move as entries are added.
        if (query.After is { } after)
        {
            entries = entries.Where(entry => entry.Id > after);
        }

        if (query.References is { } target)
        {
            entries = entries.Where(entry => context.EntryReferences
                .Any(reference => reference.EntryId == entry.Id && reference.Version == entry.PublishedVersion && reference.TargetId == target));
        }

        var rows = await entries
            .OrderBy(entry => entry.Id)
            .Take(query.Limit + 1)
            .Select(entry => new { entry.Id, Version = entry.PublishedVersion!.Value })
            .ToListAsync(cancellationToken);

        var page = rows.Take(query.Limit).ToList();
        var resolved = await EntryResolution.ResolveAsync(
            context,
            [.. page.Select(row => new VersionToResolve(row.Id, type.Slug, row.Version))],
            NoProposals,
            cancellationToken);

        return Result<PublishedPage>.Success(new PublishedPage(
            [.. resolved.Select(entry => Item(entry, type))],
            rows.Count > query.Limit ? page[^1].Id : null));
    }

    public Task<Result<PublishedItem>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return ReadAsync(id, proposals: null, cancellationToken);
    }

    public async Task<Result<PublishedItem>> GetAsync(string token, Guid entryId, CancellationToken cancellationToken = default)
    {
        var proposals = await ProposalsAsync(token, cancellationToken);
        return proposals is null ? PreviewRefused<PublishedItem>() : await ReadAsync(entryId, proposals, cancellationToken);
    }

    public async Task<Result<PreviewChanges>> ChangesAsync(string token, CancellationToken cancellationToken = default)
    {
        var proposals = await ProposalsAsync(token, cancellationToken);
        if (proposals is null)
        {
            return PreviewRefused<PreviewChanges>();
        }

        var changed = proposals.Items.Where(id => proposals.Versions[id] is not null).ToList();
        var entries = await context.Entries
            .AsNoTracking()
            .Where(row => changed.Contains(row.Id) && row.SiteId == SiteId && row.DeletedAt == null)
            .Select(row => new { row.Id, row.ContentTypeId })
            .ToListAsync(cancellationToken);

        var types = await TypesAsync(null, null, cancellationToken);
        var readable = await permissions.ReadableTypesAsync(cancellationToken);
        var shown = changed
            .Join(entries, id => id, entry => entry.Id, (id, entry) => (Id: id, Type: types[entry.ContentTypeId]))
            .Where(entry => readable(entry.Type.Slug))
            .ToList();

        var resolved = await EntryResolution.ResolveAsync(
            context,
            [.. shown.Select(entry => new VersionToResolve(entry.Id, entry.Type.Slug, proposals.Versions[entry.Id]!.Value))],
            proposals.Versions,
            cancellationToken);

        return Result<PreviewChanges>.Success(new PreviewChanges(
            proposals.ChangeSetId,
            [.. resolved.Select((entry, index) => Item(entry, shown[index].Type))]));
    }

    public async Task<IReadOnlyList<PublishedSummary>> IndexAsync(CancellationToken cancellationToken = default)
    {
        var types = await TypesAsync(null, null, cancellationToken);

        // The stored documents, not resolved views: a title is a text field of the entry itself, never a resolved reference.
        var rows = await (
            from entry in context.Entries.AsNoTracking()
            join version in context.EntryVersions.AsNoTracking()
                on new { EntryId = entry.Id, Version = entry.PublishedVersion!.Value } equals new { version.EntryId, version.Version }
            where entry.SiteId == SiteId && entry.PublishedVersion != null && entry.DeletedAt == null
            select new { entry.Id, entry.ContentTypeId, version.Version, version.Fields })
            .ToListAsync(cancellationToken);

        var readable = await permissions.ReadableTypesAsync(cancellationToken);

        return rows
            .Select(row => (Row: row, Type: types[row.ContentTypeId]))
            .Where(item => readable(item.Type.Slug))
            .Select(item =>
            {
                using var fields = JsonDocument.Parse(item.Row.Fields);
                return new PublishedSummary(
                    item.Row.Id,
                    item.Type.Slug,
                    item.Type.Name,
                    item.Row.Version,
                    EntryMarkdown.Title(fields.RootElement, item.Type.Definition));
            })
            .OrderBy(summary => summary.ContentType, StringComparer.Ordinal)
            .ThenBy(summary => summary.Id)
            .ToList();
    }

    // A set's proposal wins over the published version, and a set that unpublishes the entry hides it, as publishing would.
    private async Task<Result<PublishedItem>> ReadAsync(Guid id, Proposals? proposals, CancellationToken cancellationToken)
    {
        var entry = await context.Entries
            .AsNoTracking()
            .Where(row => row.Id == id && row.SiteId == SiteId && row.DeletedAt == null)
            .Select(row => new { row.ContentTypeId, row.PublishedVersion })
            .SingleOrDefaultAsync(cancellationToken);
        var version = proposals is not null && proposals.Versions.TryGetValue(id, out var proposed) ? proposed : entry?.PublishedVersion;
        if (entry is null || version is null)
        {
            return Result<PublishedItem>.Failure(ContentErrorCodes.EntryNotFound, $"There is no published entry {id} in this site.");
        }

        var type = (await TypesAsync(null, entry.ContentTypeId, cancellationToken))[entry.ContentTypeId];
        var allowed = await permissions.CheckContentAsync(type.Slug, AgentOperation.Read, cancellationToken: cancellationToken);
        if (!allowed.IsSuccess)
        {
            return Result<PublishedItem>.Failure(allowed.Errors);
        }

        var resolved = await EntryResolution.ResolveAsync(
            context,
            [new VersionToResolve(id, type.Slug, version.Value)],
            proposals?.Versions ?? NoProposals,
            cancellationToken);
        return Result<PublishedItem>.Success(Item(resolved[0], type));
    }

    // The entry and entity versions the token's set proposes, or null when the token does not work (API-04).
    private async Task<Proposals?> ProposalsAsync(string token, CancellationToken cancellationToken)
    {
        if (!PreviewTokenKeys.IsWellFormed(token))
        {
            return null;
        }

        var hash = PreviewTokenKeys.Hash(token);
        var found = await (
            from preview in context.PreviewTokens.AsNoTracking()
            join set in context.ChangeSets.AsNoTracking() on preview.ChangeSetId equals set.Id
            where preview.TokenHash == hash && preview.SiteId == SiteId
            select new { preview.ExpiresAt, preview.RevokedAt, set.Id, set.State })
            .SingleOrDefaultAsync(cancellationToken);

        // Compared here, because SQLite cannot compare stored offsets in a query.
        if (found is null || found.RevokedAt is not null || found.ExpiresAt <= clock.GetUtcNow() || !PreviewTokenKeys.Previewable.Contains(found.State))
        {
            return null;
        }

        var operations = await context.ChangeSetOperations
            .AsNoTracking()
            .Where(operation => operation.ChangeSetId == found.Id && (operation.ItemKind == ItemKind.Entry || operation.ItemKind == ItemKind.Entity))
            .OrderBy(operation => operation.Position)
            .Select(operation => new { operation.ItemId, operation.ProposedVersion })
            .ToListAsync(cancellationToken);

        return new Proposals(
            found.Id,
            [.. operations.Select(operation => operation.ItemId)],
            operations.ToDictionary(operation => operation.ItemId, operation => operation.ProposedVersion));
    }

    private static Result<T> PreviewRefused<T>()
    {
        return Result<T>.Failure(
            DeliveryErrorCodes.PreviewTokenInvalid,
            "The preview token does not work: it is unknown, expired or revoked, or its change set was published, rejected or reverted.");
    }

    // The site's content types with their current definitions, which a published version is read against: every type, the one
    // with the slug, or the one with the id.
    private async Task<Dictionary<Guid, PublishedType>> TypesAsync(string? slug, Guid? id, CancellationToken cancellationToken)
    {
        var rows = await (
            from type in context.ContentTypes.AsNoTracking()
            join version in context.ContentTypeVersions.AsNoTracking()
                on new { ContentTypeId = type.Id, type.Version } equals new { version.ContentTypeId, version.Version }
            where type.SiteId == SiteId && (slug == null || type.Slug == slug) && (id == null || type.Id == id)
            select new { type.Id, type.Slug, type.Name, version.Definition })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(row => row.Id, row => new PublishedType(row.Id, row.Slug, row.Name, ReadDefinition(row.Slug, row.Definition)));
    }

    private static ContentTypeDefinition ReadDefinition(string slug, string json)
    {
        var definition = ContentTypeDefinition.Parse(json);

        return definition.IsSuccess
            ? definition.Value
            : throw new InvalidOperationException(
                $"The current definition of content type '{slug}' is stored in a form that cannot be read: "
                + string.Join("; ", definition.Errors.Select(error => error.Message)));
    }

    private static PublishedItem Item(ResolvedEntry entry, PublishedType type)
    {
        return new PublishedItem(entry.EntryId, type.Slug, type.Name, type.Definition, entry.Version, entry.Fields, entry.Unresolved);
    }

    private sealed record PublishedType(Guid Id, string Slug, string Name, ContentTypeDefinition Definition);

    // Items are in the order of the set's operations; a null version is an unpublish.
    private sealed record Proposals(Guid ChangeSetId, IReadOnlyList<Guid> Items, IReadOnlyDictionary<Guid, int?> Versions);
}
