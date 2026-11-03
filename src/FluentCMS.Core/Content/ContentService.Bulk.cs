using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content.Hooks;
using FluentCMS.Core.Content.Query;
using FluentCMS.Hooks;
using FluentCMS.Localization;

namespace FluentCMS.Core.Content;

// Bulk is the single calls in a loop, so nothing a single call enforces can be skipped, and each entry commits, fails
// and is told on its own. A filter is read in full before the first write, so one over its cap writes nothing.
internal sealed partial class ContentService
{
    public async Task<BulkResult> BulkAsync(string type, BulkRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(request);

        // Inside another transaction a failed entry could take the ones before it down, and none would be told until it commits.
        if (context.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException("A bulk operation runs each entry in a transaction of its own, so it cannot run inside another.");
        }

        ThrowIfInvalid(request);
        var write = await BeginAsync(type, OperationOf(request.Operation), cancellationToken, request.Locale);
        var steps = request.Operation == BulkOperation.Create
            ? request.Entries!.Select((entry, index) => new BulkStep(index, null, entry, null)).ToList()
            : request.Items is { } items
                ? items.Select((item, index) => new BulkStep(index, item.Id, item.Patch, item.ExpectedVersion)).ToList()
                : await SelectAsync(write, request, cancellationToken);

        List<BulkItemResult> results = [];
        HashSet<Guid> seen = [];
        var cancelled = false;
        foreach (var step in steps)
        {
            cancelled = cancelled || cancellationToken.IsCancellationRequested;
            if (cancelled)
            {
                results.Add(new BulkItemResult(step.Index, step.Id, BulkItemStatus.NotRun));
            }
            else if (step.Id is { } id && !seen.Add(id))
            {
                results.Add(new BulkItemResult(step.Index, id, BulkItemStatus.Duplicate));
            }
            else
            {
                try
                {
                    results.Add(await RunAsync(type, request, step, cancellationToken));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // An entry commits only at the end of its own call, so one cancelled inside it left nothing behind.
                    cancelled = true;
                    results.Add(new BulkItemResult(step.Index, step.Id, BulkItemStatus.NotRun));
                }
                catch (Exception exception) when (exception is EntryNotFoundException or ContentAccessDeniedException or VersionConflictException or ValidationException or HookRejectedException)
                {
                    results.Add(new BulkItemResult(step.Index, step.Id, BulkItemStatus.Failed) { Problem = exception });
                }
            }
        }

        return new BulkResult(results, cancelled);
    }

    private async Task<BulkItemResult> RunAsync(string type, BulkRequest request, BulkStep step, CancellationToken cancellationToken)
    {
        var id = step.Id.GetValueOrDefault();
        switch (request.Operation)
        {
            case BulkOperation.Create:
                return Succeeded(step, await CreateAsync(type, step.Values!, cancellationToken));
            case BulkOperation.Update:
                return Succeeded(step, await PatchAsync(type, id, step.Values ?? ContentCopy.Of(request.Patch!), step.ExpectedVersion!.Value, cancellationToken));
            case BulkOperation.Publish:
                var published = await PublishAsync(type, id, request.Locale, step.ExpectedVersion, cancellationToken);
                return Succeeded(step, published.Entry) with { Warnings = published.Warnings };
            case BulkOperation.Unpublish:
                return Succeeded(step, await UnpublishAsync(type, id, request.Locale, cancellationToken));
            default:
                await DeleteAsync(type, id, cancellationToken);
                return new BulkItemResult(step.Index, id, BulkItemStatus.Succeeded);
        }
    }

    private static BulkItemResult Succeeded(BulkStep step, ContentDocument entry) =>
        new(step.Index, (Guid)entry["id"]!, BulkItemStatus.Succeeded) { Version = entry["version"] as int? };

    // Only entries the principal may read as drafts reach the selection, through the engine and its row rule, and of those
    // only the ones its grant for the operation reaches. Selecting runs to the end, so the cap is never met partway.
    private async Task<List<BulkStep>> SelectAsync(WriteScope write, BulkRequest request, CancellationToken cancellationToken)
    {
        var operation = OperationOf(request.Operation);
        List<BulkStep> steps = [];
        string? cursor = null;
        do
        {
            var query = new ContentQuery
            {
                Filter = request.Filter,
                Select = ["id", "version"],
                Status = QueryStatus.Draft,
                Site = write.Site,
                Locale = write.Locale,
                Fallback = LocaleFallback.None,
                Page = new PageRequest { Size = queryOptions.MaxPageSize, WithTotal = false },
                Cursor = cursor,
            };

            var page = await engine.ExecuteAsync(write.Type.Name, query, cancellationToken);
            foreach (var entry in page.Items)
            {
                var id = (Guid)entry["id"]!;
                if (!await evaluator.CanAccessEntryAsync(write.Operation, write.Type, operation, id, write.Target, QueryStatus.Draft, cancellationToken))
                {
                    continue;
                }

                if (steps.Count == request.Cap)
                {
                    throw new ValidationException([new ValidationError("cap", QueryErrorCodes.LimitExceeded, $"The filter selects more than the cap of {request.Cap} entries.")]);
                }

                var version = request.Operation is BulkOperation.Update ? (int?)(int)entry["version"]! : null;
                steps.Add(new BulkStep(steps.Count, id, null, version));
            }

            cursor = page.NextCursor;
        }
        while (cursor is not null);

        return steps;
    }

    private static ContentOperation OperationOf(BulkOperation operation) => operation switch
    {
        BulkOperation.Create => ContentOperation.Create,
        BulkOperation.Update => ContentOperation.Update,
        BulkOperation.Publish => ContentOperation.Publish,
        BulkOperation.Unpublish => ContentOperation.Unpublish,
        BulkOperation.Delete => ContentOperation.Delete,
        _ => throw new ValidationException([new ValidationError("operation", QueryErrorCodes.InvalidParameter, $"{operation} is not a bulk operation.")]),
    };

    // A value the operation does not use is refused rather than ignored, so a request never means less than it says.
    private static void ThrowIfInvalid(BulkRequest request)
    {
        List<ValidationError> errors = [];
        void Refuse(string path, string message, string code = QueryErrorCodes.InvalidParameter) => errors.Add(new ValidationError(path, code, message));

        var operation = request.Operation;
        OperationOf(operation);
        if (operation == BulkOperation.Create)
        {
            if (request.Entries is not { Count: > 0 } entries || entries.Any(entry => entry is null))
            {
                Refuse("entries", "A bulk create needs entries.");
            }
            else if (entries.Count > BulkRequest.MaxItems)
            {
                Refuse("entries", $"A bulk request holds at most {BulkRequest.MaxItems} entries.", QueryErrorCodes.LimitExceeded);
            }

            if (request.Items is not null || request.Filter is not null || request.Cap is not null || request.Patch is not null || request.Locale is not null)
            {
                Refuse("", "A bulk create takes entries and nothing else.");
            }
        }
        else
        {
            if (request.Entries is not null)
            {
                Refuse("entries", "Only a bulk create takes entries.");
            }

            if ((request.Items is null) == (request.Filter is null))
            {
                Refuse("", "A bulk request names its entries either by id or by filter.");
            }

            if (request.Locale is not null && operation is not (BulkOperation.Publish or BulkOperation.Unpublish))
            {
                Refuse("locale", "Only a bulk publish or unpublish takes a locale.");
            }
        }

        if (request.Items is { } items)
        {
            if (items.Count is 0 or > BulkRequest.MaxItems)
            {
                Refuse("items", $"A bulk request holds from 1 to {BulkRequest.MaxItems} items.", QueryErrorCodes.LimitExceeded);
            }

            for (var index = 0; index < items.Count; index++)
            {
                var item = items[index];
                if (item is null)
                {
                    Refuse($"items[{index}]", "An item names an entry.");
                    continue;
                }

                if ((item.Patch is null) == (operation == BulkOperation.Update))
                {
                    Refuse($"items[{index}].patch", operation == BulkOperation.Update ? "An update needs its patch." : "Only an update takes a patch.");
                }

                if (item.ExpectedVersion is null && operation == BulkOperation.Update)
                {
                    Refuse($"items[{index}].expectedVersion", "An update needs the version it was based on.");
                }
                else if (item.ExpectedVersion is not null && operation is not (BulkOperation.Update or BulkOperation.Publish))
                {
                    Refuse($"items[{index}].expectedVersion", "Only an update or a publish takes an expected version.");
                }
            }

            if (request.Patch is not null)
            {
                Refuse("patch", "Items by id carry their own patch.");
            }
        }

        if (request.Filter is not null)
        {
            if (request.Cap is null)
            {
                Refuse("cap", "A bulk request by filter needs a cap.");
            }
            else if (request.Cap is < 1 or > BulkRequest.MaxItems)
            {
                Refuse("cap", $"The cap is from 1 to {BulkRequest.MaxItems}.", QueryErrorCodes.LimitExceeded);
            }

            if ((request.Patch is null) == (operation == BulkOperation.Update))
            {
                Refuse("patch", operation == BulkOperation.Update ? "An update by filter needs its patch." : "Only an update takes a patch.");
            }
        }
        else if (request.Cap is not null && operation != BulkOperation.Create)
        {
            Refuse("cap", "Only a bulk request by filter takes a cap.");
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    private sealed record BulkStep(int Index, Guid? Id, ContentDocument? Values, int? ExpectedVersion);
}
