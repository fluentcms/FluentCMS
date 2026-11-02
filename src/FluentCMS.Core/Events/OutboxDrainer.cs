using FluentCMS.Content;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Events;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Events;

// Delivers the rows of cms_outbox at least once. A delivery is claimed by a conditional update that only one of any
// number of instances wins, and holds a lease, so that a worker that died frees its rows and one that is merely slow is
// not delivered to twice while it still holds them.
internal sealed class OutboxDrainer(
    IServiceScopeFactory scopes,
    OutboxRegistry registry,
    OutboxOptions options,
    IOperationContextAccessor operation,
    TimeProvider time,
    ILogger<OutboxDrainer> logger)
{
    private readonly string _owner = $"{Environment.MachineName}/{Guid.NewGuid():N}";

    // The number of deliveries that succeeded.
    public async Task<int> DrainAsync(CancellationToken cancellationToken = default)
    {
        var delivered = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var batch = await ClaimAsync(cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            foreach (var message in batch)
            {
                if (await DeliverAsync(message, cancellationToken))
                {
                    delivered++;
                }
            }
        }

        await PruneAsync(cancellationToken);
        return delivered;
    }

    private async Task<List<OutboxMessage>> ClaimAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();

        var now = time.GetUtcNow();
        var leaseUntil = now + options.LeaseDuration;

        var candidates = await Due(context, now)
            .OrderBy(message => message.OccurredAt).ThenBy(message => message.Position).ThenBy(message => message.Id)
            .Select(message => message.Id)
            .Take(options.BatchSize)
            .ToListAsync(cancellationToken);

        List<Guid> claimed = [];
        foreach (var id in candidates)
        {
            var won = await Due(context, now).Where(message => message.Id == id).ExecuteUpdateAsync(
                message => message
                    .SetProperty(row => row.LeaseOwner, _owner)
                    .SetProperty(row => row.LeaseUntil, leaseUntil)
                    .SetProperty(row => row.Attempts, row => row.Attempts + 1),
                cancellationToken);

            if (won == 1)
            {
                claimed.Add(id);
            }
        }

        if (claimed.Count == 0)
        {
            return [];
        }

        return await context.Set<OutboxMessage>().AsNoTracking()
            .Where(message => claimed.Contains(message.Id))
            .OrderBy(message => message.OccurredAt).ThenBy(message => message.Position).ThenBy(message => message.Id)
            .ToListAsync(cancellationToken);
    }

    private static IQueryable<OutboxMessage> Due(SystemDbContext context, DateTimeOffset now) =>
        context.Set<OutboxMessage>().Where(message =>
            message.Status == OutboxStatus.Pending
            && message.AvailableAt <= now
            && (message.LeaseUntil == null || message.LeaseUntil < now));

    private async Task<bool> DeliverAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        try
        {
            var registration = registry.Find(message.Handler)
                ?? throw new InvalidOperationException($"No outbox handler is registered as '{message.Handler}'.");
            var @event = registration.Read(message.Payload)
                ?? throw new InvalidOperationException($"The payload of the event for '{message.Handler}' is null.");

            await using (var scope = scopes.CreateAsyncScope())
            {
                var previous = operation.Current;
                operation.Current = OperationOf(message);
                try
                {
                    await registration.Invoke(scope.ServiceProvider, @event, new EventDelivery(message.Id, message.Attempts), cancellationToken);
                }
                finally
                {
                    operation.Current = previous;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await SettleAsync(message, row => row
                .SetProperty(column => column.Attempts, column => column.Attempts - 1)
                .SetProperty(column => column.LeaseOwner, (string?)null)
                .SetProperty(column => column.LeaseUntil, (DateTimeOffset?)null));
            return false;
        }
        catch (Exception exception)
        {
            await FailAsync(message, exception);
            return false;
        }

        var now = time.GetUtcNow();
        await SettleAsync(message, row => row
            .SetProperty(column => column.Status, OutboxStatus.Delivered)
            .SetProperty(column => column.DeliveredAt, now)
            .SetProperty(column => column.LastError, (string?)null)
            .SetProperty(column => column.LeaseOwner, (string?)null)
            .SetProperty(column => column.LeaseUntil, (DateTimeOffset?)null));
        return true;
    }

    private async Task FailAsync(OutboxMessage message, Exception exception)
    {
        var error = JobErrorText.ForStorage(exception, message.Payload);
        var now = time.GetUtcNow();

        if (message.Attempts >= options.MaxAttempts)
        {
            EventLog.DeliveryGaveUp(logger, message.Id, message.Handler, message.Attempts, JobErrorText.ForLog(exception, message.Payload));
            await SettleAsync(message, row => row
                .SetProperty(column => column.Status, OutboxStatus.Failed)
                .SetProperty(column => column.LastError, error)
                .SetProperty(column => column.LeaseOwner, (string?)null)
                .SetProperty(column => column.LeaseUntil, (DateTimeOffset?)null));
            return;
        }

        EventLog.DeliveryFailed(logger, message.Id, message.Handler, message.Attempts, JobErrorText.ForLog(exception, message.Payload));
        var retryAt = now + options.RetryAfter(message.Attempts);
        await SettleAsync(message, row => row
            .SetProperty(column => column.AvailableAt, retryAt)
            .SetProperty(column => column.LastError, error)
            .SetProperty(column => column.LeaseOwner, (string?)null)
            .SetProperty(column => column.LeaseUntil, (DateTimeOffset?)null));
    }

    // Matches nothing once the lease has gone to another instance. A write that fails leaves the row leased, so it is
    // delivered again when the lease ends.
    private async Task SettleAsync(OutboxMessage message, Action<UpdateSettersBuilder<OutboxMessage>> update)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
            await context.Set<OutboxMessage>()
                .Where(row => row.Id == message.Id && row.Status == OutboxStatus.Pending && row.LeaseOwner == _owner && row.Attempts == message.Attempts)
                .ExecuteUpdateAsync(update);
        }
        catch (Exception exception)
        {
            EventLog.SettleFailed(logger, exception, message.Id);
        }
    }

    private async Task PruneAsync(CancellationToken cancellationToken)
    {
        var before = time.GetUtcNow() - options.Retention;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
            await context.Set<OutboxMessage>()
                .Where(row => row.Status == OutboxStatus.Delivered && row.DeliveredAt < before)
                .ExecuteDeleteAsync(cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            EventLog.DrainFailed(logger, exception);
        }
    }

    private OperationContext OperationOf(OutboxMessage message) =>
        new(Principal.System, Origin.Job, new SiteId(message.SiteId ?? DefaultSite.Id), DefaultSite.Locale, ClientId: null, message.EventId.ToString(), time.GetUtcNow());
}
