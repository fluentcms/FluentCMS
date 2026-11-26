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
// documents' query filter. A removal changes what the entries answer, so it evicts their tags as a publish would, and with
// the tag header on its event names their types' too.
internal sealed class ContentReferences(
    CmsDbContext database,
    SchemaModel schema,
    IAuthorizationEvaluator evaluator,
    IOperationContextAccessor operations,
    IOutputCacheStore store,
    IEventBus events,
    DeliveryCacheOptions delivery,
    EntryStore entryStore,
    IDataProvider provider,
    ContentWriteOptions writeOptions) : IContentReferences
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

        // In a caller's transaction, such as a content hook's, the rows are held where it removes them, and a deadlock has
        // ended that transaction, which is the caller's to handle.
        var (entries, removed) = database.Database.CurrentTransaction is null
            ? await InOwnTransactionAsync(() => RemoveHeldAsync(targetType, targetId, cancellationToken), cancellationToken)
            : await RemoveHeldAsync(targetType, targetId, cancellationToken);
        if (removed > 0 && delivery.Invalidates)
        {
            var types = delivery.TagHeader is not null ? await TypeTagsAsync(entries, CancellationToken.None) : [];
            string[] tags = [.. entries.Select(CacheTags.Entry)];
            foreach (var tag in tags)
            {
                await store.EvictByTagAsync(tag, CancellationToken.None);
            }

            await events.PublishAsync(new CacheInvalidatedEvent([.. tags, .. types]), CancellationToken.None);
        }

        return removed;
    }

    // A save replaces its entry's references under the hold of its rows, so the references of an entry go only once its
    // cms_document rows are held, in the lock order of a content write (spec 4.5): each entry's rows by primary key, ordinal by
    // locale, the entries by id. A row added since the read is held after them, so that a save of a new locale, which deletes
    // the shared references too, cannot cross the removal. The references of an entry that began to refer to the target since
    // the read stay, as they would had its save come after.
    private async Task<(List<Guid> Entries, int Removed)> RemoveHeldAsync(string targetType, Guid targetId, CancellationToken cancellationToken)
    {
        var entries = await database.References
            .Where(reference => reference.TargetType == targetType && reference.TargetId == targetId)
            .Select(reference => reference.SourceDocumentId)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (entries.Count == 0)
        {
            return (entries, 0);
        }

        HashSet<Guid> held = [];
        while (true)
        {
            var rows = await database.Documents.IgnoreQueryFilters().AsNoTracking()
                .Where(document => entries.Contains(document.DocumentId))
                .ToListAsync(cancellationToken);
            var fresh = rows.Where(row => !held.Contains(row.Id)).OrderBy(row => row.DocumentId).ThenBy(row => row.Locale, StringComparer.Ordinal).ToList();
            if (fresh.Count == 0)
            {
                break;
            }

            foreach (var row in fresh)
            {
                await entryStore.HoldAsync(row, cancellationToken);
                held.Add(row.Id);
            }
        }

        var removed = await database.References
            .Where(reference => reference.TargetType == targetType && reference.TargetId == targetId && entries.Contains(reference.SourceDocumentId))
            .ExecuteDeleteAsync(cancellationToken);
        return (entries, removed);
    }

    // As a content write in a transaction of its own: a deadlock victim runs again after a pause (#2959), and a wait past the
    // lock timeout is not run again (#3573).
    private async Task<T> InOwnTransactionAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken)
    {
        for (var deadlocks = 0; ; )
        {
            try
            {
                await using var transaction = await provider.BeginWriteAsync(database, writeOptions.LockTimeout, cancellationToken);
                var result = await work();
                await transaction.CommitAsync(CancellationToken.None);
                return result;
            }
            catch (Exception exception) when (deadlocks < ContentService.DeadlockReruns && provider.IsDeadlock(exception))
            {
                await ContentService.PauseAfterDeadlockAsync(++deadlocks, cancellationToken);
            }
            catch (Exception exception) when (provider.IsLockTimeout(exception))
            {
                throw new EntryBusyException(writeOptions.LockTimeout, exception);
            }
        }
    }

    // A CDN's copy whose tag header would have passed its budget carries the tags of its entries' types in place of theirs,
    // while the server's copy carries every entry's (4.10), so only the event names them.
    private async Task<string[]> TypeTagsAsync(List<Guid> entries, CancellationToken cancellationToken)
    {
        var rows = await database.Documents.AsNoTracking()
            .Where(document => entries.Contains(document.DocumentId))
            .Select(document => new { document.Type, document.SiteId })
            .Distinct()
            .ToListAsync(cancellationToken);
        return
        [
            .. rows
                .Select(row => schema.FindContentType(row.Type) is { } type ? CacheTags.Type(type, row.SiteId ?? Guid.Empty) : null)
                .OfType<string>()
                .Distinct(),
        ];
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
