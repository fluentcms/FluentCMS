using FluentCMS.Activity;
using FluentCMS.Content;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.ActivityLog;

internal sealed class ActivityQuery(SystemDbContext context, IOperationContextAccessor operation) : IActivityQuery
{
    public async Task<PagedResult<ActivityItem>> QueryAsync(ActivityFilter? filter, PageRequest? page, CancellationToken cancellationToken = default)
    {
        var site = (operation.Current ?? throw new InvalidOperationException("The activity log is read for the site of an operation, and none is running.")).Site.Value;
        var (number, offset, size) = Paging(page);

        var rows = Filtered(context.Set<ActivityRow>().AsNoTracking(), filter ?? new ActivityFilter(), site);
        long? total = page?.WithTotal ?? true ? await rows.LongCountAsync(cancellationToken) : null;

        var found = await rows
            .OrderByDescending(row => row.OccurredAt).ThenByDescending(row => row.Id)
            .Skip(offset).Take(size)
            .ToListAsync(cancellationToken);

        return new PagedResult<ActivityItem>([.. found.Select(ToItem)], total, number, size, nextCursor: null);
    }

    private static IQueryable<ActivityRow> Filtered(IQueryable<ActivityRow> rows, ActivityFilter filter, Guid site)
    {
        rows = filter.IncludeInstance ? rows.Where(row => row.SiteId == site || row.SiteId == null) : rows.Where(row => row.SiteId == site);

        if (filter.ActorId is { } actorId)
        {
            rows = rows.Where(row => row.ActorId == actorId);
        }

        if (filter.ActorKind is { } actorKind)
        {
            rows = rows.Where(row => row.ActorType == actorKind);
        }

        if (filter.Origin is { } origin)
        {
            rows = rows.Where(row => row.Origin == origin);
        }

        if (!string.IsNullOrEmpty(filter.Action))
        {
            var action = filter.Action.ToLowerInvariant();
            rows = action.EndsWith('*')
                ? ByPrefix(rows, action[..^1])
                : rows.Where(row => row.Action == action);
        }

        if (!string.IsNullOrEmpty(filter.TargetType))
        {
            var targetType = filter.TargetType;
            rows = rows.Where(row => row.TargetType == targetType);
        }

        if (!string.IsNullOrEmpty(filter.TargetId))
        {
            var targetId = filter.TargetId;
            rows = rows.Where(row => row.TargetId == targetId);
        }

        if (filter.From is { } from)
        {
            var start = from.ToUniversalTime();
            rows = rows.Where(row => row.OccurredAt >= start);
        }

        if (filter.To is { } to)
        {
            var end = to.ToUniversalTime();
            rows = rows.Where(row => row.OccurredAt < end);
        }

        return rows;
    }

    private static IQueryable<ActivityRow> ByPrefix(IQueryable<ActivityRow> rows, string prefix) =>
        prefix.Length == 0 ? rows : rows.Where(row => row.Action.StartsWith(prefix));

    private static (int? Number, int Offset, int Size) Paging(PageRequest? page)
    {
        var size = page?.Size ?? ActivityOptions.DefaultPageSize;
        if (size < 1 || size > ActivityOptions.MaxPageSize)
        {
            throw new ArgumentException($"{ActivityErrorCodes.InvalidPage}: the page size is {size}; it must be from 1 to {ActivityOptions.MaxPageSize}.", nameof(page));
        }

        if (page?.Number is not null && page.Offset is not null)
        {
            throw new ArgumentException($"{ActivityErrorCodes.InvalidPage}: a page is read by number or by offset, not both.", nameof(page));
        }

        if (page?.Offset is { } offset)
        {
            return offset >= 0
                ? (null, offset, size)
                : throw new ArgumentException($"{ActivityErrorCodes.InvalidPage}: the offset is {offset}; it must not be negative.", nameof(page));
        }

        var number = page?.Number ?? 1;
        return number >= 1
            ? (number, (number - 1) * size, size)
            : throw new ArgumentException($"{ActivityErrorCodes.InvalidPage}: the page number is {number}; it starts at 1.", nameof(page));
    }

    private static ActivityItem ToItem(ActivityRow row) =>
        new(
            row.Id,
            row.SiteId,
            row.OccurredAt,
            row.ActorType,
            row.ActorId,
            row.ActorName,
            row.Origin,
            row.ClientId,
            row.Action,
            row.TargetType,
            row.TargetId,
            row.TargetLabel,
            row.Locale,
            ActivityText.ReadDetails(row.Details),
            row.CorrelationId,
            row.Ip);
}
