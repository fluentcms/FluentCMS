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

// An agent that reads through here keeps to its scope (MCP-05); people and anonymous readers see every published entry.
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

        var typeId = await context.ContentTypes
            .AsNoTracking()
            .Where(row => row.SiteId == SiteId && row.Slug == query.ContentType)
            .Select(row => (Guid?)row.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (typeId is null)
        {
            return Result<PublishedPage>.Failure(SchemaErrorCodes.ContentTypeNotFound, $"There is no content type '{query.ContentType}' in this site.");
        }

        var entries = context.Entries
            .AsNoTracking()
            .Where(entry => entry.SiteId == SiteId && entry.ContentTypeId == typeId && entry.PublishedVersion != null && entry.DeletedAt == null);

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
            [.. page.Select(row => new VersionToResolve(row.Id, query.ContentType, row.Version))],
            cancellationToken);

        return Result<PublishedPage>.Success(new PublishedPage(
            [.. resolved.Select(Item)],
            rows.Count > query.Limit ? page[^1].Id : null));
    }

    public async Task<Result<PublishedItem>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entry = await (
            from row in context.Entries.AsNoTracking()
            join type in context.ContentTypes.AsNoTracking() on row.ContentTypeId equals type.Id
            where row.Id == id && row.SiteId == SiteId && row.PublishedVersion != null && row.DeletedAt == null
            select new { type.Slug, Version = row.PublishedVersion!.Value })
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

        var resolved = await EntryResolution.ResolveAsync(context, [new VersionToResolve(id, entry.Slug, entry.Version)], cancellationToken);
        return Result<PublishedItem>.Success(Item(resolved[0]));
    }

    private static PublishedItem Item(ResolvedEntry entry)
    {
        return new PublishedItem(entry.EntryId, entry.ContentType, entry.Version, entry.Fields, entry.Unresolved);
    }
}
