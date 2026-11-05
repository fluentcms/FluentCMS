using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content.Query;
using FluentCMS.Localization;
using FluentCMS.Schema;

namespace FluentCMS.Core.Content;

internal sealed partial class ContentService
{
    public Task<PagedResult<ContentDocument>> QueryTrashAsync(string type, ContentQuery query, CancellationToken cancellationToken = default) =>
        QueryTrashAsync(type, query, everySystemField: false, cancellationToken);

    // The trash is read like drafts are: by the engine, so the read grant, its row rule, its locale and field limits and
    // the site all hold. Restore or purge on the type is what lets a principal look. What it cannot read is not listed,
    // and a type it cannot read answers as an unknown one does.
    public async Task<PagedResult<ContentDocument>> QueryTrashAsync(string typeName, ContentQuery query, bool everySystemField, CancellationToken cancellationToken)
    {
        using var span = Span("query-trash", typeName);

        ArgumentNullException.ThrowIfNull(typeName);
        ArgumentNullException.ThrowIfNull(query);

        var operation = Current();
        var type = schema.FindContentType(typeName)
            ?? throw new ValidationException([new ValidationError("type", QueryErrorCodes.UnknownField, $"'{typeName}' is not a content type that can be read.")]);

        var site = query.Site ?? operation.Site;
        var locale = await ResolveLocaleAsync(site, query.Locale ?? operation.Locale, cancellationToken);
        if (locale == ILocaleResolver.All)
        {
            throw new ValidationException([new ValidationError("locale", QueryErrorCodes.InvalidParameter, "The trash is read in one locale.")]);
        }

        var target = new AccessTarget(type.Scope == ContentScope.Global ? operation.Site : site, locale);
        var permissions = await evaluator.ResolveAsync(operation, cancellationToken);
        if (!permissions.Allows(type, ContentOperation.Restore, target) && !permissions.Allows(type, ContentOperation.Purge, target))
        {
            if (!permissions.Allows(type, ContentOperation.Read, target))
            {
                throw new ValidationException([new ValidationError("type", QueryErrorCodes.UnknownField, $"'{typeName}' is not a content type that can be read.")]);
            }

            throw new ContentAccessDeniedException(type.Name, CorePermissions.OperationName(ContentOperation.Restore));
        }

        var page = await ReadAsync(typeName, query with { Fallback = LocaleFallback.None }, null, everySystemField, cancellationToken, trashed: true);
        if (page.Items.Count == 0)
        {
            return page;
        }

        var marks = await store.TrashMarksAsync([.. page.Items.Select(entry => (Guid)entry["id"]!)], type.Localized ? locale : "", cancellationToken);
        var namesPeople = permissions.CanReadField(type, "updatedBy", target);
        foreach (var entry in page.Items)
        {
            if (marks.TryGetValue((Guid)entry["id"]!, out var mark))
            {
                entry["deletedAt"] = mark.At;
                if (namesPeople)
                {
                    entry["deletedBy"] = mark.By;
                }
            }
        }

        return page;
    }

    // Only an entry in the trash can be purged, so a live one is not found, and one the row rule of the purge grant
    // excludes is not found either, whatever the id. Every version goes with it, published ones included.
    public Task PurgeAsync(string type, Guid id, CancellationToken cancellationToken = default) =>
        PurgeAsync(type, id, trashedBefore: null, cancellationToken);

    // The purge job passes the cutoff it listed the entry by, and an entry put in the trash again since is not found.
    internal async Task PurgeAsync(string type, Guid id, DateTimeOffset? trashedBefore, CancellationToken cancellationToken)
    {
        using var span = Span("purge", type);

        ArgumentNullException.ThrowIfNull(type);

        var write = await BeginAsync(type, ContentOperation.Purge, cancellationToken);
        await InTransactionAsync(
            async () =>
            {
                var (documents, row, _) = await ReachAllLocalesAsync(write, ContentOperation.Purge, id, trashed: true, cancellationToken);

                if (await store.PurgeAsync(write.Type, id, trashedBefore, cancellationToken) != documents.Count)
                {
                    throw new EntryNotFoundException(write.Type.Name, id);
                }

                _pending.AddRange(documents.Select(document => new EntryChange(EntryChangeKind.Purged, write.Type.Name, id, document.Locale, document.DraftVersion, write.Operation)));
                return row;
            },
            cancellationToken);
    }
}
