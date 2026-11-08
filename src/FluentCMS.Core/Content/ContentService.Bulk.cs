using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
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
    internal const string UnexpectedFailure = "The operation failed unexpectedly. The log of the host has the cause.";

    // Rows the principal may read and the operation's rule lets through, but that a single entry check still refuses,
    // are skipped; a filter that reaches this many of them for each one the cap allows is refused rather than walked.
    private const int SkippedPerCapped = 10;

    // Held as one write, so that no other write of the scope can move _commits between an entry and its result.
    public Task<BulkResult> BulkAsync(string type, BulkRequest request, CancellationToken cancellationToken = default) =>
        OneAtATimeAsync(() => RunBulkAsync(type, request, cancellationToken));

    private async Task<BulkResult> RunBulkAsync(string type, BulkRequest request, CancellationToken cancellationToken)
    {
        using var span = Span("bulk", type);

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
                var commits = _commits;
                try
                {
                    results.Add(await RunAsync(type, request, step, cancellationToken));
                }
                catch (Exception exception) when (_commits != commits && _committed is { } row)
                {
                    // What follows the commit only reads the entry back, and cannot undo it.
                    ContentLog.BulkReadBackFailed(logger, exception, step.Index, request.Operation, type);
                    results.Add(new BulkItemResult(step.Index, row.DocumentId, BulkItemStatus.Succeeded) { Version = request.Operation == BulkOperation.Delete ? null : row.DraftVersion });
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // An entry commits only at the end of its own call, so one cancelled inside it left nothing behind.
                    cancelled = true;
                    results.Add(new BulkItemResult(step.Index, step.Id, BulkItemStatus.NotRun));
                }
                catch (Exception exception) when (ProblemOf(exception) is { } problem)
                {
                    results.Add(new BulkItemResult(step.Index, step.Id, BulkItemStatus.Failed) { Problem = problem });
                }
                catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    // A bug or a database failure says nothing the caller may read, and its message can hold a value of the entry.
                    ContentLog.BulkItemFailed(logger, exception, ApiErrorCodes.InternalError, step.Index, request.Operation, type);
                    results.Add(new BulkItemResult(step.Index, step.Id, BulkItemStatus.Failed) { Problem = new BulkProblem(ApiErrorCodes.InternalError, UnexpectedFailure) });
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

    // The refusals of the single call, with the codes its problem details carry; the HTTP layer adds the status and the title.
    // Any other exception is not one to report.
    internal static BulkProblem? ProblemOf(Exception exception) => exception switch
    {
        EntryNotFoundException => new BulkProblem(ApiErrorCodes.NotFound, exception.Message),
        ContentAccessDeniedException => new BulkProblem(ApiErrorCodes.Forbidden, exception.Message),
        VersionConflictException conflict => new BulkProblem(ApiErrorCodes.VersionConflict, conflict.Message) { Current = conflict.Current },
        ValidationException validation => new BulkProblem(
            validation.Errors.Select(error => error.Code).Distinct().Count() == 1 ? validation.Errors[0].Code : ApiErrorCodes.ValidationFailed,
            validation.Errors.Count == 1 ? validation.Errors[0].Message : $"The request has {validation.Errors.Count} errors; see errors.")
        {
            Errors = validation.Errors,
        },
        HookRejectedException rejected => new BulkProblem(rejected.Code, rejected.Message),
        _ => null,
    };

    private static BulkItemResult Succeeded(BulkStep step, ContentDocument entry) =>
        new(step.Index, (Guid)entry["id"]!, BulkItemStatus.Succeeded) { Version = entry["version"] as int? };

    // The engine selects the drafts the principal may read and that the row rule of the operation lets through, both in
    // one query, and each is then checked as the single call checks it. Selecting runs to the end before anything is
    // written, so the cap is never met partway; what it skips is bounded too, so a broad filter cannot walk the table.
    private async Task<List<BulkStep>> SelectAsync(WriteScope write, BulkRequest request, CancellationToken cancellationToken)
    {
        var operation = OperationOf(request.Operation);
        var rule = write.Permissions.RowRuleOf(write.Type, operation, write.Target);
        var cap = request.Cap!.Value;
        List<BulkStep> steps = [];
        var skipped = 0;
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
                Page = new PageRequest { Size = Math.Min(queryOptions.MaxPageSize, cap + 1), WithTotal = false },
                Cursor = cursor,
            };

            var page = await engine.ExecuteWithRuleAsync(write.Type.Name, query, rule, cancellationToken);
            foreach (var entry in page.Items)
            {
                var id = (Guid)entry["id"]!;
                if (!await evaluator.CanAccessEntryAsync(write.Operation, write.Type, operation, id, write.Target, QueryStatus.Draft, cancellationToken))
                {
                    if (++skipped > SkippedPerCapped * cap)
                    {
                        throw new ValidationException([new ValidationError("filter", QueryErrorCodes.LimitExceeded, $"The filter reaches more than {SkippedPerCapped * cap} entries that cannot be changed; narrow it.")]);
                    }

                    continue;
                }

                if (steps.Count == cap)
                {
                    throw new ValidationException([new ValidationError("cap", QueryErrorCodes.LimitExceeded, $"The filter selects more than the cap of {cap} entries.")]);
                }

                // An update and a publish act on the version the filter matched, so a draft changed since is a conflict.
                // Unpublish and delete take no version: they act on what matched, and both can be undone.
                var version = request.Operation is BulkOperation.Update or BulkOperation.Publish ? (int?)(int)entry["version"]! : null;
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
