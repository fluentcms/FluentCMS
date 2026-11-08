using FluentCMS.Content;
using FluentCMS.Core.Api.Delivery;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Caching;
using FluentCMS.Core.Storage;
using FluentCMS.Events;
using FluentCMS.Schema;
using FluentCMS.Sites;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Content;

// What cms_reference says refers to a target outside content, for the media plugin's usage and deletes. A read asks the
// evaluator about each type, site, state and locale at once, as a read of those entries would, and leaves out a field the
// caller may not read; a non-localized value is asked about in the locale of the operation. The trash is hidden by the
// documents' query filter. A removal changes what the entries answer, so it evicts their tags as a publish would.
internal sealed class ContentReferences(
    CmsDbContext database,
    SchemaModel schema,
    IAuthorizationEvaluator evaluator,
    IOperationContextAccessor operations,
    IOutputCacheStore store,
    IEventBus events,
    DeliveryCacheOptions delivery) : IContentReferences
{
    public async Task<IReadOnlyList<ContentReference>> FindAsync(string targetType, Guid targetId, CancellationToken cancellationToken = default)
    {
        Check(targetType);

        var operation = operations.Current ?? throw new InvalidOperationException("References are read inside an operation, whose principal decides what it may read.");
        var rows = await database.References.AsNoTracking()
            .Where(reference => reference.TargetType == targetType && reference.TargetId == targetId)
            .Select(reference => new { reference.SourceType, reference.SourceDocumentId, reference.State, reference.Locale, reference.Field })
            .Distinct()
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(row => row.SourceDocumentId).Distinct().ToList();
        var sites = (await database.Documents.AsNoTracking()
                .Where(document => ids.Contains(document.DocumentId))
                .Select(document => new { document.DocumentId, document.Type, document.SiteId })
                .ToListAsync(cancellationToken))
            .DistinctBy(document => (document.DocumentId, document.Type))
            .ToDictionary(document => (document.DocumentId, document.Type), document => document.SiteId);

        var permissions = await evaluator.ResolveAsync(operation, cancellationToken);
        List<ContentReference> found = [];
        var live = rows.Where(row => sites.ContainsKey((row.SourceDocumentId, row.SourceType)));
        foreach (var group in live.GroupBy(row => (row.SourceType, Site: sites[(row.SourceDocumentId, row.SourceType)], row.State, row.Locale)))
        {
            if (schema.FindContentType(group.Key.SourceType) is not { } type)
            {
                continue;
            }

            var site = type.Scope == ContentScope.Site && group.Key.Site is { } own ? new SiteId(own) : operation.Site;
            var target = new AccessTarget(site, group.Key.Locale.Length > 0 ? group.Key.Locale : operation.Locale);
            var status = group.Key.State == ContentState.Published ? QueryStatus.Published : QueryStatus.Draft;
            var readable = await evaluator.AccessibleEntriesAsync(operation, type, ContentOperation.Read, [.. group.Select(row => row.SourceDocumentId).Distinct()], target, status, cancellationToken);
            found.AddRange(group
                .Where(row => readable.Contains(row.SourceDocumentId) && permissions.CanReadField(type, row.Field.Split('.')[0], target))
                .Select(row => new ContentReference(type.Name, row.SourceDocumentId, status, row.Locale.Length > 0 ? row.Locale : null, row.Field)));
        }

        return
        [
            .. found
                .OrderBy(reference => reference.Type, StringComparer.Ordinal)
                .ThenBy(reference => reference.EntryId)
                .ThenBy(reference => reference.Status)
                .ThenBy(reference => reference.Locale, StringComparer.Ordinal)
                .ThenBy(reference => reference.Field, StringComparer.Ordinal),
        ];
    }

    public Task<int> CountAsync(string targetType, Guid targetId, CancellationToken cancellationToken = default)
    {
        Check(targetType);

        return database.References.AsNoTracking()
            .Where(reference => reference.TargetType == targetType && reference.TargetId == targetId
                && database.Documents.Any(document => document.DocumentId == reference.SourceDocumentId && document.Type == reference.SourceType))
            .Select(reference => new { reference.SourceType, reference.SourceDocumentId })
            .Distinct()
            .CountAsync(cancellationToken);
    }

    public async Task<int> RemoveAsync(string targetType, Guid targetId, CancellationToken cancellationToken = default)
    {
        Check(targetType);

        var references = database.References.Where(reference => reference.TargetType == targetType && reference.TargetId == targetId);
        var entries = await references.Select(reference => reference.SourceDocumentId).Distinct().ToListAsync(cancellationToken);
        var removed = await references.ExecuteDeleteAsync(cancellationToken);
        if (removed > 0 && delivery.OutputCache)
        {
            string[] tags = [.. entries.Select(CacheTags.Entry)];
            foreach (var tag in tags)
            {
                await store.EvictByTagAsync(tag, CancellationToken.None);
            }

            await events.PublishAsync(new CacheInvalidatedEvent(tags), CancellationToken.None);
        }

        return removed;
    }

    private void Check(string targetType)
    {
        ArgumentException.ThrowIfNullOrEmpty(targetType);
        if (schema.FindContentType(targetType) is not null)
        {
            throw new ArgumentException($"'{targetType}' is a content type; the references between entries are the content service's.", nameof(targetType));
        }
    }
}
