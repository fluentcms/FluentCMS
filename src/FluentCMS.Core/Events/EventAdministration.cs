using System.Globalization;
using FluentCMS.Activity;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Events;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Events;

internal sealed record DeadLettersPurged(string Handler, int Removed);

// The failed outbox deliveries of the instance for the Management API, with jobs.manage for the instance as dead-lettered
// jobs have: an error can name internals of the host whatever site its event came from, and the payload is never shown.
// Every retry and removal is an activity row of no site for the same reason; a purge by handler is one row with its count.
internal sealed class EventAdministration(
    SystemDbContext context,
    IEventDeadLetters deadLetters,
    OutboxOptions options,
    ManagementAuthority authority,
    IActivityLog activity,
    IOperationContextAccessor accessor,
    TimeProvider time)
{
    private sealed record Delivery(Guid Id, string Handler, string EventType, Guid EventId, string Status);

    public async Task<IReadOnlyList<FailedEventDelivery>> DeadLettersAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        await RequireAsync(cancellationToken);
        return await deadLetters.ListAsync((page - 1) * pageSize, pageSize, cancellationToken);
    }

    public async Task RetryAsync(Guid id, CancellationToken cancellationToken)
    {
        await RequireAsync(cancellationToken);
        var delivery = await FindAsync(id, cancellationToken);
        if (delivery.Status != OutboxStatus.Failed || !await deadLetters.RetryAsync(id, cancellationToken))
        {
            throw await RefusedAsync(id, "Only a failed delivery is retried.", cancellationToken);
        }

        await LogAsync("event_delivery.retried", delivery, cancellationToken);
    }

    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken)
    {
        await RequireAsync(cancellationToken);
        var delivery = await FindAsync(id, cancellationToken);
        if (delivery.Status != OutboxStatus.Failed || !await deadLetters.RemoveAsync(id, cancellationToken))
        {
            throw await RefusedAsync(id, "Only a failed delivery is removed; a pending or delivered one is the drain's.", cancellationToken);
        }

        await LogAsync("event_delivery.removed", delivery, cancellationToken);
    }

    public async Task<DeadLettersPurged> PurgeAsync(string? handler, CancellationToken cancellationToken)
    {
        await RequireAsync(cancellationToken);
        if (string.IsNullOrEmpty(handler))
        {
            throw new ValidationException([new ValidationError("handler", ValidationErrorCodes.Required, "Name the handler whose failed deliveries are removed.")]);
        }

        var removed = await OutboxPruneJob.DeleteAsync(context, row => row.Status == OutboxStatus.Failed && row.Handler == handler, options.PruneBatchSize, cancellationToken);
        if (removed > 0)
        {
            var entry = new ActivityEntry("event_delivery.purged")
            {
                TargetType = "event_handler",
                TargetId = handler,
                TargetLabel = handler,
                Details = new Dictionary<string, string?> { ["removed"] = removed.ToString(CultureInfo.InvariantCulture) },
            };
            await activity.WriteAsync(entry, Source(), cancellationToken);
        }

        return new DeadLettersPurged(handler, removed);
    }

    private ValueTask RequireAsync(CancellationToken cancellationToken) =>
        authority.RequireAsync(CorePermissions.JobsManage, site: null, cancellationToken);

    private async Task<Delivery> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await context.Set<OutboxMessage>().AsNoTracking()
            .Where(row => row.Id == id)
            .Select(row => new Delivery(row.Id, row.Handler, row.EventType, row.EventId, row.Status))
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw NotFound(id);

    // Read again, since the row may have gone between the read and the write.
    private async Task<ValidationException> RefusedAsync(Guid id, string message, CancellationToken cancellationToken) =>
        await context.Set<OutboxMessage>().AnyAsync(row => row.Id == id, cancellationToken)
            ? new ValidationException([new ValidationError("id", EventErrorCodes.DeliveryNotFailed, message)])
            : NotFound(id);

    private static ValidationException NotFound(Guid id) => new([new ValidationError("id", EventErrorCodes.DeliveryNotFound, $"There is no event delivery {id:D}.")]);

    private async Task LogAsync(string action, Delivery delivery, CancellationToken cancellationToken)
    {
        var entry = new ActivityEntry(action)
        {
            TargetType = "event_delivery",
            TargetId = delivery.Id.ToString("D"),
            TargetLabel = delivery.Handler,
            Details = new Dictionary<string, string?>
            {
                ["eventType"] = delivery.EventType,
                ["eventId"] = delivery.EventId.ToString("D"),
            },
        };
        await activity.WriteAsync(entry, Source(), cancellationToken);
    }

    private EventSource Source() =>
        (accessor.Current is { } operation ? EventSource.From(operation) : EventSource.FromSystem(time.GetUtcNow())) with { SiteId = Guid.Empty };
}
