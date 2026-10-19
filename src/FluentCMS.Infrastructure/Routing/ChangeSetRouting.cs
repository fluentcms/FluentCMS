using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Results;
using FluentCMS.Core.Routing;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Infrastructure.Routing;

// A read: the set, its diff for the changed fields and the content type of each item, evaluated against the active rules.
internal sealed class ChangeSetRouting(
    FluentCmsDbContext context,
    IChangeSetDirectory changeSets,
    IRoutingRuleDirectory rules) : IChangeSetRouting
{
    public async Task<Result<RoutingDecision>> RouteAsync(Guid changeSetId, CancellationToken cancellationToken = default)
    {
        var set = await changeSets.GetAsync(changeSetId, cancellationToken);
        if (!set.IsSuccess)
        {
            return Result<RoutingDecision>.Failure(set.Errors);
        }

        var diff = await changeSets.DiffAsync(changeSetId, cancellationToken);
        if (!diff.IsSuccess)
        {
            return Result<RoutingDecision>.Failure(diff.Errors);
        }

        // Read from the diff's own operations, so an operation added between the two reads still has its content type.
        var contentTypes = await ContentTypesAsync(diff.Value.Items.Select(item => item.Operation).ToList(), cancellationToken);
        var items = diff.Value.Items
            .Select(item => new RoutingItem(
                item.Operation.ItemKind,
                item.Operation.ItemId,
                item.Operation.Kind,
                contentTypes.GetValueOrDefault(item.Operation.ItemId),
                item.Fields.Select(field => field.Name).ToHashSet(StringComparer.Ordinal),
                item.Properties.Select(property => property.Name).ToHashSet(StringComparer.Ordinal)))
            .ToList();

        var active = await rules.ListActiveAsync(cancellationToken);
        return Result<RoutingDecision>.Success(RoutingEvaluator.Evaluate(active, new RoutingFacts(items, set.Value.Initiator)));
    }

    // The slug of each item's content type: a schema item's own, and the type of an entry or an entity.
    private async Task<Dictionary<Guid, string>> ContentTypesAsync(IReadOnlyList<ChangeOperation> operations, CancellationToken cancellationToken)
    {
        var schema = operations.Where(operation => operation.ItemKind == ItemKind.Schema).Select(operation => operation.ItemId).ToList();
        var entries = operations
            .Where(operation => operation.ItemKind is ItemKind.Entry or ItemKind.Entity)
            .Select(operation => operation.ItemId)
            .ToList();

        var types = await context.ContentTypes
            .AsNoTracking()
            .Where(type => schema.Contains(type.Id))
            .Select(type => new { type.Id, type.Slug })
            .ToListAsync(cancellationToken);
        var entryTypes = await (
                from entry in context.Entries.AsNoTracking()
                join type in context.ContentTypes.AsNoTracking() on entry.ContentTypeId equals type.Id
                where entries.Contains(entry.Id)
                select new { entry.Id, type.Slug })
            .ToListAsync(cancellationToken);

        return types.Concat(entryTypes).ToDictionary(row => row.Id, row => row.Slug);
    }
}
