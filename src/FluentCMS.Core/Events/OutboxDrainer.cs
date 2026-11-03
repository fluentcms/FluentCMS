using FluentCMS.Content;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Events;

// Delivers the rows of cms_outbox at least once. A batch is claimed with a conditional update that only one of any number
// of instances wins per row, and holds a lease. Claiming costs no attempt, since a row whose lease runs out before its turn
// comes was never tried: the worker starts an attempt, and takes its lease again, with one conditional update before each
// delivery, which matches nothing once another instance has the row. A delivery is cut off before its lease can end, so that
// a worker never delivers on a lease it has lost, and a write of the outcome is conditional on the claim in the same way.
// A handler that has to be abandoned may still be running, on a thread of its own; nothing here can stop it.
internal sealed class OutboxDrainer(
    IServiceScopeFactory scopes,
    OutboxRegistry registry,
    OutboxOptions options,
    IOperationContextAccessor operation,
    TimeProvider time,
    ILogger<OutboxDrainer> logger)
{
    private const string ExhaustedError = "The worker of the last attempt stopped without finishing it, and no attempts are left.";

    private readonly string _owner = $"{Environment.MachineName}/{Guid.NewGuid():N}";

    public Task<int> DrainAsync(CancellationToken cancellationToken = default) => DrainAsync(prune: false, cancellationToken);

    // The number of deliveries that succeeded.
    public async Task<int> DrainAsync(bool prune, CancellationToken cancellationToken)
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
                if (cancellationToken.IsCancellationRequested)
                {
                    await ReleaseAsync(message, delay: null, started: false);
                    continue;
                }

                if (await DeliverAsync(message, cancellationToken))
                {
                    delivered++;
                }
            }
        }

        if (prune)
        {
            await PruneAsync(cancellationToken);
        }

        return delivered;
    }

    private async Task<List<OutboxMessage>> ClaimAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();

        var now = time.GetUtcNow();
        var leaseUntil = now + options.LeaseDuration;

        // A row whose attempts are used up and whose worker is gone has no one left to settle it: a delivery that kills its
        // process would otherwise be tried for ever.
        var exhausted = await Open(context, now).Where(message => message.Attempts >= options.MaxAttempts).ExecuteUpdateAsync(
            message => message
                .SetProperty(row => row.Status, OutboxStatus.Failed)
                .SetProperty(row => row.LastError, ExhaustedError)
                .SetProperty(row => row.LeaseOwner, (string?)null)
                .SetProperty(row => row.LeaseUntil, (DateTimeOffset?)null),
            cancellationToken);

        if (exhausted > 0)
        {
            EventLog.Exhausted(logger, exhausted);
        }

        var candidates = await Open(context, now)
            .OrderBy(message => message.Sequence).ThenBy(message => message.Id)
            .Select(message => message.Id)
            .Take(options.BatchSize)
            .ToListAsync(cancellationToken);

        List<Guid> claimed = [];
        foreach (var id in candidates)
        {
            var won = await Open(context, now).Where(message => message.Id == id).ExecuteUpdateAsync(
                message => message
                    .SetProperty(row => row.LeaseOwner, _owner)
                    .SetProperty(row => row.LeaseUntil, leaseUntil),
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
            .OrderBy(message => message.Sequence).ThenBy(message => message.Id)
            .ToListAsync(cancellationToken);
    }

    private static IQueryable<OutboxMessage> Open(SystemDbContext context, DateTimeOffset now) =>
        context.Set<OutboxMessage>().Where(message =>
            message.Status == OutboxStatus.Pending
            && message.AvailableAt <= now
            && (message.LeaseUntil == null || message.LeaseUntil < now));

    private static IQueryable<OutboxMessage> Held(SystemDbContext context, OutboxMessage message, string owner) =>
        context.Set<OutboxMessage>().Where(row =>
            row.Id == message.Id && row.Status == OutboxStatus.Pending && row.LeaseOwner == owner && row.Attempts == message.Attempts);

    private async Task<bool> DeliverAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var registration = registry.Find(message.Handler);
        if (registration is null)
        {
            // Another instance may have the handler: this one is mid-deploy, or the plugin is gone. The row waits, and a
            // plugin that stays removed leaves its rows pending.
            EventLog.HandlerMissing(logger, message.Id, message.Handler);
            await ReleaseAsync(message, options.MissingHandlerDelay, started: false);
            return false;
        }

        if (!await StartAsync(message))
        {
            EventLog.LeaseLost(logger, message.Id, message.Handler);
            return false;
        }

        try
        {
            var @event = registration.Read(message.Payload)
                ?? throw new InvalidOperationException($"The payload of the event for '{message.Handler}' is null.");

            await using var scope = scopes.CreateAsyncScope();
            using var limit = new CancellationTokenSource(options.DeliveryTimeout, time);
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, limit.Token);

            var previous = operation.Current;
            operation.Current = await EventOperation.CreateAsync(scope.ServiceProvider, @event.Source, message.EventId, message.SiteId, time.GetUtcNow());
            try
            {
                // On a thread of its own, since a handler that blocks before its first await would never hand control back
                // to the timeout.
                var delivery = new EventDelivery(message.Id, message.Attempts);
                await Task.Run(() => registration.Invoke(scope.ServiceProvider, @event, delivery, attempt.Token).AsTask(), CancellationToken.None)
                    .WaitAsync(options.DeliveryTimeout, time, cancellationToken);
            }
            finally
            {
                operation.Current = previous;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ReleaseAsync(message, delay: null, started: true);
            return false;
        }
        catch (TimeoutException)
        {
            EventLog.DeliveryAbandoned(logger, message.Id, message.Handler, options.DeliveryTimeout);
            await FailAsync(message, $"The handler did not finish within {options.DeliveryTimeout}.");
            return false;
        }
        catch (Exception exception)
        {
            await FailAsync(message, OutboxErrorText.Of(exception, message.Payload));
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

    // Starts an attempt and takes the lease again, in one conditional update. False when the row is no longer this worker's.
    private async Task<bool> StartAsync(OutboxMessage message)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
            var until = time.GetUtcNow() + options.LeaseDuration;
            var started = await Held(context, message, _owner).ExecuteUpdateAsync(row => row
                .SetProperty(column => column.LeaseUntil, until)
                .SetProperty(column => column.Attempts, column => column.Attempts + 1)) == 1;

            if (started)
            {
                message.Attempts++;
            }

            return started;
        }
        catch (Exception exception)
        {
            EventLog.SettleFailed(logger, exception, message.Id);
            return false;
        }
    }

    private async Task FailAsync(OutboxMessage message, string error)
    {
        if (message.Attempts >= options.MaxAttempts)
        {
            EventLog.DeliveryGaveUp(logger, message.Id, message.Handler, message.Attempts, error);
            await SettleAsync(message, row => row
                .SetProperty(column => column.Status, OutboxStatus.Failed)
                .SetProperty(column => column.LastError, error)
                .SetProperty(column => column.LeaseOwner, (string?)null)
                .SetProperty(column => column.LeaseUntil, (DateTimeOffset?)null));
            return;
        }

        EventLog.DeliveryFailed(logger, message.Id, message.Handler, message.Attempts, error);
        var retryAt = time.GetUtcNow() + options.RetryAfter(message.Attempts);
        await SettleAsync(message, row => row
            .SetProperty(column => column.AvailableAt, retryAt)
            .SetProperty(column => column.LastError, error)
            .SetProperty(column => column.LeaseOwner, (string?)null)
            .SetProperty(column => column.LeaseUntil, (DateTimeOffset?)null));
    }

    // An attempt that was started is given back, since the handler did not run to an end.
    private Task ReleaseAsync(OutboxMessage message, TimeSpan? delay, bool started)
    {
        var availableAt = delay is { } wait ? time.GetUtcNow() + wait : message.AvailableAt;
        var back = started ? 1 : 0;
        return SettleAsync(message, row => row
            .SetProperty(column => column.Attempts, column => column.Attempts - back)
            .SetProperty(column => column.AvailableAt, availableAt)
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
            await Held(context, message, _owner).ExecuteUpdateAsync(update);
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
        catch (OperationCanceledException)
        {
        }
    }
}
