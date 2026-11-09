using FluentCMS.Content;
using FluentCMS.Core.Plugins;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Events;

// Delivers the rows of cms_outbox at least once. A batch is claimed with one conditional update over its ids, which only one
// of any number of instances wins for each row, and holds a lease under an owner of that claim alone, so that two drains of
// one instance never take each other's rows when they read back what they won. Claiming costs no attempt, since a row whose lease runs out before its turn
// comes was never tried: the worker starts an attempt, and takes its lease again, with one conditional update before each
// delivery, which matches nothing once another instance has the row. A delivery is cut off before its lease can end, so that
// a worker never delivers on a lease it has lost, and a write of the outcome is conditional on the claim in the same way.
// The attempt counts only once the handler is entered: a cut-off that comes before the handler starts, as when the thread pool
// is starved, gives the attempt back and leaves the row for a later drain. A handler that has to be abandoned once it has
// started may still be running, on a thread of its own; nothing here can stop it.
// A row of a plugin that is now disabled in the row's site was recorded while it was enabled, so it is owed to the plugin: it
// waits, as a row with no handler does, without using an attempt, and is delivered once the plugin is enabled there again.
// Either waits OrphanAfter at most, from when its event happened; then it is left as failed, so that a plugin that is gone
// for good leaves rows in the dead-letter list rather than rows claimed again for ever.
internal sealed class OutboxDrainer(
    IServiceScopeFactory scopes,
    OutboxRegistry registry,
    PluginState plugins,
    OutboxOptions options,
    IOperationContextAccessor operation,
    TimeProvider time,
    ILogger<OutboxDrainer> logger)
{
    private const string ExhaustedError = "The worker of the last attempt stopped without finishing it, and no attempts are left.";

    private readonly string _instance = $"{Environment.MachineName}/{Guid.NewGuid():N}";

    private long _claims;

    // On a thread of its own, since a handler that blocks before its first await would never hand control back to the
    // cut-off. A test replaces it to hold a handler back past its cut-off.
    internal Func<Func<Task>, Task> HandOff { get; set; } = work => Task.Run(work);

    // The number of deliveries that succeeded.
    public async Task<int> DrainAsync(CancellationToken cancellationToken = default)
    {
        var delivered = 0;
        // A row whose handler never started waits for a later drain, so a starved thread pool cannot keep one drain going.
        HashSet<Guid> deferred = [];
        while (!cancellationToken.IsCancellationRequested)
        {
            var batch = await ClaimAsync(deferred, cancellationToken);
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

                if (await DeliverAsync(message, deferred, cancellationToken))
                {
                    delivered++;
                }
            }
        }

        return delivered;
    }

    private async Task<List<OutboxMessage>> ClaimAsync(HashSet<Guid> deferred, CancellationToken cancellationToken)
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
                .SetProperty(row => row.AvailableAt, now)
                .SetProperty(row => row.LeaseOwner, (string?)null)
                .SetProperty(row => row.LeaseUntil, (DateTimeOffset?)null),
            cancellationToken);

        if (exhausted > 0)
        {
            EventLog.Exhausted(logger, exhausted);
        }

        var candidates = await Open(context, now)
            .Where(message => !deferred.Contains(message.Id))
            .OrderBy(message => message.Sequence).ThenBy(message => message.Id)
            .Select(message => message.Id)
            .Take(options.BatchSize)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return [];
        }

        // The update checks every condition of the read again, so a row another drain took in between is left to it.
        var owner = $"{_instance}/{Interlocked.Increment(ref _claims)}";
        var won = await Open(context, now).Where(message => candidates.Contains(message.Id)).ExecuteUpdateAsync(
            message => message
                .SetProperty(row => row.LeaseOwner, owner)
                .SetProperty(row => row.LeaseUntil, leaseUntil),
            cancellationToken);

        if (won == 0)
        {
            return [];
        }

        return await context.Set<OutboxMessage>().AsNoTracking()
            .Where(message => candidates.Contains(message.Id) && message.Status == OutboxStatus.Pending && message.LeaseOwner == owner)
            .OrderBy(message => message.Sequence).ThenBy(message => message.Id)
            .ToListAsync(cancellationToken);
    }

    private static IQueryable<OutboxMessage> Open(SystemDbContext context, DateTimeOffset now) =>
        context.Set<OutboxMessage>().Where(message =>
            message.Status == OutboxStatus.Pending
            && message.AvailableAt <= now
            && (message.LeaseUntil == null || message.LeaseUntil <= now));

    private static IQueryable<OutboxMessage> Held(SystemDbContext context, OutboxMessage message) =>
        context.Set<OutboxMessage>().Where(row =>
            row.Id == message.Id && row.Status == OutboxStatus.Pending && row.LeaseOwner == message.LeaseOwner && row.Attempts == message.Attempts);

    private async Task<bool> DeliverAsync(OutboxMessage message, HashSet<Guid> deferred, CancellationToken cancellationToken)
    {
        var registration = registry.Find(message.Handler);
        if (registration is null)
        {
            // Another instance may have the handler: this one is mid-deploy, or the plugin is gone. The row waits, and a
            // plugin that stays removed leaves its rows failed once they are old enough.
            if (Orphaned(message))
            {
                await OrphanAsync(message, $"No handler is registered as '{message.Handler}', and the delivery waited longer than {options.OrphanAfter} for one.");
                return false;
            }

            EventLog.HandlerMissing(logger, message.Id, message.Handler);
            await ReleaseAsync(message, options.MissingHandlerDelay, started: false);
            return false;
        }

        string? disabled;
        try
        {
            disabled = await plugins.DisabledOutboxHandlerAsync(message.Handler, message.SiteId, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Given back for a later drain, so that the rest of the batch is not left leased.
            PluginLog.StateUnavailable(logger, exception);
            deferred.Add(message.Id);
            await ReleaseAsync(message, delay: null, started: false);
            return false;
        }

        if (disabled is not null)
        {
            if (Orphaned(message))
            {
                await OrphanAsync(message, $"The handler '{message.Handler}' belongs to the plugin '{disabled}', which is disabled in the site {message.SiteId:D}, and the delivery waited longer than {options.OrphanAfter} for it to be enabled.");
                return false;
            }

            PluginLog.DeliveryWaits(logger, message.Id, message.Handler, disabled, message.SiteId!.Value);
            await ReleaseAsync(message, options.MissingHandlerDelay, started: false);
            return false;
        }

        if (!await StartAsync(message))
        {
            EventLog.LeaseLost(logger, message.Id, message.Handler);
            return false;
        }

        var state = new HandOffState();
        try
        {
            var @event = registration.Read(message.Payload)
                ?? throw new InvalidOperationException($"The payload of the event for '{message.Handler}' is null.");

            var scope = scopes.CreateAsyncScope();
            var limit = new CancellationTokenSource(options.DeliveryTimeout, time);
            var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, limit.Token);
            Task? run = null;

            var previous = operation.Current;
            try
            {
                operation.Current = await EventOperation.CreateAsync(scope.ServiceProvider, @event.Source, message.EventId, message.SiteId, time.GetUtcNow());
                var delivery = new EventDelivery(message.Id, message.Attempts);
                run = HandOff(() => state.TryStart() ? registration.Invoke(scope.ServiceProvider, @event, delivery, attempt.Token).AsTask() : Task.CompletedTask);
                await run.WaitAsync(options.DeliveryTimeout, time, cancellationToken);
            }
            finally
            {
                operation.Current = previous;

                // So that a handler which has not started by now never starts on the scope.
                state.Abandon();
                await EndAsync(run, scope, limit, attempt, message);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ReleaseAsync(message, delay: null, started: true);
            return false;
        }
        catch (TimeoutException)
        {
            // Read here and not in a filter, which would run before the finally that abandons the hand-off.
            if (state.WasAbandoned)
            {
                EventLog.HandOffAbandoned(logger, message.Id, message.Handler, options.DeliveryTimeout);
                deferred.Add(message.Id);
                await ReleaseAsync(message, delay: null, started: true);
                return false;
            }

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
            var started = await Held(context, message).ExecuteUpdateAsync(row => row
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
            await SettleFailedAsync(message, error);
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

    private bool Orphaned(OutboxMessage message) => message.OccurredAt <= time.GetUtcNow() - options.OrphanAfter;

    // Moved to the dead-letter list without using an attempt, since no handler ran.
    private Task OrphanAsync(OutboxMessage message, string error)
    {
        EventLog.Orphaned(logger, message.Id, message.Handler, options.OrphanAfter);
        return SettleFailedAsync(message, error);
    }

    // A failed row is never available again, so its available_at says when it failed, which FailedRetention counts from.
    private Task SettleFailedAsync(OutboxMessage message, string error)
    {
        var now = time.GetUtcNow();
        return SettleAsync(message, row => row
            .SetProperty(column => column.Status, OutboxStatus.Failed)
            .SetProperty(column => column.LastError, error)
            .SetProperty(column => column.AvailableAt, now)
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
            await Held(context, message).ExecuteUpdateAsync(update);
        }
        catch (Exception exception)
        {
            EventLog.SettleFailed(logger, exception, message.Id);
        }
    }

    // A handler that was cut off, or left by a stopping host, may still be running on its scope and its token, so they go only
    // once it ends. What it throws then comes after its delivery was settled, and is only logged.
    private async ValueTask EndAsync(Task? run, AsyncServiceScope scope, CancellationTokenSource limit, CancellationTokenSource attempt, OutboxMessage message)
    {
        if (run is not { IsCompleted: false })
        {
            // Observed, in case it faulted after its cut-off.
            _ = run?.Exception;
            await DisposeAsync(scope, limit, attempt, message);
            return;
        }

        _ = run.ContinueWith(
            async ended =>
            {
                if (ended.Exception?.InnerException is { } exception and not OperationCanceledException)
                {
                    EventLog.AbandonedDeliveryFailed(logger, exception, message.Id, message.Handler);
                }

                await DisposeAsync(scope, limit, attempt, message);
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default).Unwrap();
    }

    private async ValueTask DisposeAsync(AsyncServiceScope scope, CancellationTokenSource limit, CancellationTokenSource attempt, OutboxMessage message)
    {
        attempt.Dispose();
        limit.Dispose();
        try
        {
            await scope.DisposeAsync();
        }
        catch (Exception exception)
        {
            EventLog.DeliveryScopeFailed(logger, exception, message.Id, message.Handler);
        }
    }

    // Decides once between the handler starting and the hand-off being abandoned, so exactly one of them happens.
    private sealed class HandOffState
    {
        private const int Pending = 0;
        private const int Started = 1;
        private const int Abandoned = 2;

        private int _state;

        public bool WasAbandoned => Volatile.Read(ref _state) == Abandoned;

        public bool TryStart() => Interlocked.CompareExchange(ref _state, Started, Pending) == Pending;

        public void Abandon() => Interlocked.CompareExchange(ref _state, Abandoned, Pending);
    }
}
