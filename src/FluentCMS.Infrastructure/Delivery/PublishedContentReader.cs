using System.Text.Json;
using FluentCMS.Core.Agents;
using FluentCMS.Core.Content;
using FluentCMS.Core.Delivery;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Content;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Infrastructure.Delivery;

// An agent that reads through here keeps to its scope (MCP-05); people and anonymous readers see every published entry. Each item
// carries the definition its version was written against, so a revision of the type waiting for review changes nothing it shows.
internal sealed class PublishedContentReader(FluentCmsDbContext context, IAgentPermissions permissions) : IPublishedContent
{
    // Until multi-site (MST-01), every entry belongs to the one site (SCH-07).
    private static readonly Guid SiteId = Site.Default.Id;

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

        var type = await context.ContentTypes
            .AsNoTracking()
            .Where(row => row.SiteId == SiteId && row.Slug == query.ContentType)
            .Select(row => new { row.Id, row.Name })
            .SingleOrDefaultAsync(cancellationToken);
        if (type is null)
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

        var rows = await (
            from entry in entries
            join version in context.EntryVersions.AsNoTracking()
                on new { EntryId = entry.Id, Version = entry.PublishedVersion!.Value } equals new { version.EntryId, version.Version }
            orderby entry.Id
            select new { entry.Id, version.Version, version.ContentTypeVersion })
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken);

        var page = rows.Take(query.Limit).ToList();
        var definitions = await DefinitionsAsync([.. page.Select(row => (type.Id, row.ContentTypeVersion))], cancellationToken);
        var resolved = await EntryResolution.ResolveAsync(
            context,
            [.. page.Select(row => new VersionToResolve(row.Id, query.ContentType, row.Version))],
            cancellationToken);

        return Result<PublishedPage>.Success(new PublishedPage(
            [.. resolved.Select((entry, index) => Item(entry, type.Name, definitions[(type.Id, page[index].ContentTypeVersion)]))],
            rows.Count > query.Limit ? page[^1].Id : null));
    }

    public async Task<Result<PublishedItem>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entry = await (
            from row in context.Entries.AsNoTracking()
            join type in context.ContentTypes.AsNoTracking() on row.ContentTypeId equals type.Id
            join version in context.EntryVersions.AsNoTracking()
                on new { EntryId = row.Id, Version = row.PublishedVersion!.Value } equals new { version.EntryId, version.Version }
            where row.Id == id && row.SiteId == SiteId && row.PublishedVersion != null && row.DeletedAt == null
            select new { row.ContentTypeId, type.Slug, type.Name, version.Version, version.ContentTypeVersion })
            .SingleOrDefaultAsync(cancellationToken);
        if (entry is null)
        {
            return Result<PublishedItem>.Failure(ContentErrorCodes.EntryNotFound, $"There is no published entry {id} in this site.");
        }

        var allowed = await permissions.CheckContentAsync(entry.Slug, AgentOperation.Read, cancellationToken: cancellationToken);
        if (!allowed.IsSuccess)
        {
            return Result<PublishedItem>.Failure(allowed.Errors);
        }

        var definitions = await DefinitionsAsync([(entry.ContentTypeId, entry.ContentTypeVersion)], cancellationToken);
        var resolved = await EntryResolution.ResolveAsync(context, [new VersionToResolve(id, entry.Slug, entry.Version)], cancellationToken);
        return Result<PublishedItem>.Success(Item(resolved[0], entry.Name, definitions[(entry.ContentTypeId, entry.ContentTypeVersion)]));
    }

    public async Task<IReadOnlyList<PublishedSummary>> IndexAsync(CancellationToken cancellationToken = default)
    {
        // The stored documents, not resolved views: a title is a text field of the entry itself, never a resolved reference.
        var rows = await (
            from entry in context.Entries.AsNoTracking()
            join type in context.ContentTypes.AsNoTracking() on entry.ContentTypeId equals type.Id
            join version in context.EntryVersions.AsNoTracking()
                on new { EntryId = entry.Id, Version = entry.PublishedVersion!.Value } equals new { version.EntryId, version.Version }
            where entry.SiteId == SiteId && entry.PublishedVersion != null && entry.DeletedAt == null
            select new { entry.Id, entry.ContentTypeId, type.Slug, type.Name, version.Version, version.ContentTypeVersion, version.Fields })
            .ToListAsync(cancellationToken);

        var readable = await permissions.ReadableTypesAsync(cancellationToken);
        var shown = rows.Where(row => readable(row.Slug)).ToList();
        var definitions = await DefinitionsAsync([.. shown.Select(row => (row.ContentTypeId, row.ContentTypeVersion))], cancellationToken);

        return shown
            .Select(row =>
            {
                using var fields = JsonDocument.Parse(row.Fields);
                return new PublishedSummary(
                    row.Id,
                    row.Slug,
                    row.Name,
                    row.Version,
                    EntryMarkdown.Title(fields.RootElement, definitions[(row.ContentTypeId, row.ContentTypeVersion)]));
            })
            .OrderBy(summary => summary.ContentType, StringComparer.Ordinal)
            .ThenBy(summary => summary.Id)
            .ToList();
    }

    // The definitions of the type versions given, read in one query; every pair must be stored.
    private async Task<Dictionary<(Guid TypeId, int Version), ContentTypeDefinition>> DefinitionsAsync(
        IReadOnlyCollection<(Guid TypeId, int Version)> wanted,
        CancellationToken cancellationToken)
    {
        var pairs = wanted.ToHashSet();
        var typeIds = pairs.Select(pair => pair.TypeId).Distinct().ToList();
        var versions = pairs.Select(pair => pair.Version).Distinct().ToList();

        var rows = await context.ContentTypeVersions
            .AsNoTracking()
            .Where(row => typeIds.Contains(row.ContentTypeId) && versions.Contains(row.Version))
            .Select(row => new { row.ContentTypeId, row.Version, row.Definition })
            .ToListAsync(cancellationToken);

        return rows
            .Where(row => pairs.Contains((row.ContentTypeId, row.Version)))
            .ToDictionary(row => (row.ContentTypeId, row.Version), row => ReadDefinition(row.ContentTypeId, row.Version, row.Definition));
    }

    private static ContentTypeDefinition ReadDefinition(Guid typeId, int version, string json)
    {
        var definition = ContentTypeDefinition.Parse(json);

        return definition.IsSuccess
            ? definition.Value
            : throw new InvalidOperationException(
                $"Version {version} of content type {typeId} is stored in a form that cannot be read: "
                + string.Join("; ", definition.Errors.Select(error => error.Message)));
    }

    private static PublishedItem Item(ResolvedEntry entry, string typeName, ContentTypeDefinition definition)
    {
        return new PublishedItem(entry.EntryId, entry.ContentType, typeName, definition, entry.Version, entry.Fields, entry.Unresolved);
    }
}
