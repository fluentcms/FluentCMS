using System.Text.Json;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Content;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Builtin.Jobs;

// The background half of bulk_update_entries (MCP-08): each item's new version and its operation in the change set commit together,
// one item at a time, as the agent that asked, so its permissions, scopes and caps apply to every item. An item that fails is
// recorded and the rest go on. An item already in the set is skipped, so a retried job repeats nothing.
public sealed class BulkEntryUpdates : IJobHandler
{
    public const string Name = "bulk.entry_updates";

    private const int MaxFailuresListed = 20;

    // Short enough that the count and every listed item fit in the job's last error, which the queue keeps to 4000 characters.
    private const int MaxReasonLength = 100;

    public string Type => Name;

    public static JsonElement Payload(Guid changeSetId, IReadOnlyList<BulkEntryUpdate> items)
    {
        return JsonSerializer.SerializeToElement(new Request(changeSetId, items), JsonSerializerOptions.Web);
    }

    private static Request? Read(JsonElement payload)
    {
        try
        {
            return payload.ValueKind == JsonValueKind.Object
                && payload.Deserialize<Request>(JsonSerializerOptions.Web) is { ChangeSetId: var id, Items: not null } request
                && id != Guid.Empty
                    ? request
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<Result> RunAsync(JobContext context, CancellationToken cancellationToken)
    {
        // Another attempt reads the same payload, so one this handler cannot read is a failure, not an exception to retry.
        if (Read(context.Job.Payload) is not { } request)
        {
            return Result.Failure("payload_invalid", "A bulk update names its change set as changeSetId and its items as a list of id, baseVersion and fields.");
        }

        var services = context.Services;
        var changeSets = services.GetRequiredService<IChangeSetDirectory>();

        var set = await changeSets.GetAsync(request.ChangeSetId, cancellationToken);
        if (!set.IsSuccess)
        {
            return Result.Failure(set.Errors);
        }

        var applied = set.Value.Operations.Select(operation => operation.ItemId).ToHashSet();
        var failures = new List<Error>();
        var total = request.Items.Count;

        for (var index = 0; index < total; index++)
        {
            var item = request.Items[index];
            if (applied.Add(item.Id))
            {
                var updated = await ApplyAsync(services, request.ChangeSetId, item, cancellationToken);
                if (!updated.IsSuccess && !await AppliedMeanwhileAsync(changeSets, request.ChangeSetId, item.Id, cancellationToken))
                {
                    // One entry per item, however many problems it has, so the counts and the list are of items.
                    var reason = string.Join(" ", updated.Errors.Select(error => error.Message));
                    failures.Add(new Error(updated.Errors[0].Code, $"items[{index}] {item.Id}: {Shorten(reason)}"));
                }
            }

            var message = $"Updated {index + 1 - failures.Count} of {total}" + (failures.Count > 0 ? $"; {failures.Count} failed." : ".");
            if (!await context.ReportProgressAsync(index + 1, total, message, cancellationToken))
            {
                // Another worker has the job now and goes on from what is already in the set.
                return Result.Success();
            }
        }

        if (failures.Count == 0)
        {
            return Result.Success();
        }

        // The count first, so it is kept whatever follows.
        var counted = $"{failures.Count} of {total} items failed" + (failures.Count > MaxFailuresListed ? $"; the first {MaxFailuresListed} are listed." : ".");
        return Result.Failure([new Error("bulk_items_failed", counted), .. failures.Take(MaxFailuresListed)]);
    }

    private static string Shorten(string reason)
    {
        return reason.Length <= MaxReasonLength ? reason : string.Concat(reason.AsSpan(0, MaxReasonLength - 1), "…");
    }

    // A worker that lost its lease can commit an item after this run read the set; the item is in the set, so it has not failed.
    private static async Task<bool> AppliedMeanwhileAsync(IChangeSetDirectory changeSets, Guid changeSetId, Guid itemId, CancellationToken cancellationToken)
    {
        var set = await changeSets.GetAsync(changeSetId, cancellationToken);
        return set.IsSuccess && set.Value.Operations.Any(operation => operation.ItemId == itemId);
    }

    // The update and its operation commit together or not at all; leaving the unit of work uncommitted undoes both.
    private static async Task<Result> ApplyAsync(IServiceProvider services, Guid changeSetId, BulkEntryUpdate item, CancellationToken cancellationToken)
    {
        await using var scope = await services.GetRequiredService<IUnitOfWork>().BeginAsync(cancellationToken);

        var entry = await services.GetRequiredService<IEntryDirectory>().UpdateAsync(item.Id, item.BaseVersion, item.Fields, cancellationToken);
        if (!entry.IsSuccess)
        {
            return Result.Failure(entry.Errors);
        }

        var type = await services.GetRequiredService<IContentTypeDirectory>().GetAsync(entry.Value.ContentType, cancellationToken);
        if (!type.IsSuccess)
        {
            return Result.Failure(type.Errors);
        }

        var kind = type.Value.Definition.IsEntity ? ItemKind.Entity : ItemKind.Entry;
        var operation = entry.Value.PublishedVersion is { } published
            ? ChangeOperation.Create(OperationKind.Update, kind, entry.Value.Id, published, entry.Value.Latest.Version)
            : ChangeOperation.Create(OperationKind.Create, kind, entry.Value.Id, null, entry.Value.Latest.Version);
        if (!operation.IsSuccess)
        {
            return Result.Failure(operation.Errors);
        }

        var added = await services.GetRequiredService<IChangeSetDirectory>().AddOperationAsync(changeSetId, operation.Value, cancellationToken);
        if (!added.IsSuccess)
        {
            return Result.Failure(added.Errors);
        }

        await scope.CommitAsync(cancellationToken);
        return Result.Success();
    }

    private sealed record Request(Guid ChangeSetId, IReadOnlyList<BulkEntryUpdate> Items);
}

public sealed record BulkEntryUpdate(Guid Id, int BaseVersion, JsonElement Fields);
