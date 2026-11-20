using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
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

// Delivers the rows of cms_outbox at least once. Each handler has a lane of its own, which takes the rows of that handler in
// the order of their events, so a handler that is slow or hangs holds back its own deliveries and no other handler's; the
// order between the handlers of one event is not kept. The drain starts a lane for every handler with rows due, and looks
// again whenever a lane ends or a kick of this instance signals it, so that a new event for an idle handler does not wait
// for a slow one. A lane claims a batch with one conditional update over its ids, which only one of any number of instances
// wins for each row, and holds a lease under an owner of that claim alone, so that two drains of one instance never take
// each other's rows when they read back what they won.
// The claim counts an attempt for each row it takes, so that a delivery that kills its process uses up its attempts with no
// write of its own; a row that is not run to an end gives its attempt back. A row taken over from a worker whose lease lapsed
// is claimed alone, so that a delivery that kills its process costs the rows after it one attempt at most. The lease of a
// batch is renewed only when what is left of it would not cover one more delivery, and the rows delivered are settled
// together then and at the end of the batch, so a batch whose handler answers in time costs one write to claim it and one
// to settle it. A delivery is cut off before its lease can end, so that a worker never delivers on a lease it has lost, and
// every write of an outcome is conditional on the claim.
// A cut-off that comes before the handler starts, as when the thread pool is starved, gives the attempt back and leaves the
// row for a later drain. A handler that has to be abandoned once it has started may still be running, on a thread of its
// own; nothing here can stop it.
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

    private TaskCompletionSource _wake = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // On a thread of its own, since a handler that blocks before its first await would never hand control back to the
    // cut-off. A test replaces it to hold a handler back past its cut-off.
    internal Func<Func<Task>, Task> HandOff { get; set; } = work => Task.Run(work);

    // Asks the drains running on this instance to look again for handlers with rows due that have no lane yet.
    public void Signal() =>
        Interlocked.Exchange(ref _wake, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();

    // The number of deliveries that succeeded.
    public async Task<int> DrainAsync(CancellationToken cancellationToken = default)
    {
        var delivered = 0;
        // A row whose handler never started waits for a later drain, so a starved thread pool cannot keep one drain going.
        var deferred = new ConcurrentDictionary<Guid, byte>();
        var lanes = new Dictionary<string, Task<int>>(StringComparer.Ordinal);
        ExceptionDispatchInfo? failure = null;

        while (true)
        {
            var wake = Volatile.Read(ref _wake).Task;
            var looking = failure is null && !cancellationToken.IsCancellationRequested;
            if (looking)
            {
                try
                {
                    foreach (var handler in await DueHandlersAsync(deferred, cancellationToken))
                    {
                        if (!lanes.ContainsKey(handler))
                        {
                            lanes[handler] = Task.Run(() => LaneAsync(handler, deferred, cancellationToken), CancellationToken.None);
                        }
                    }
                }
                catch (Exception exception)
                {
                    // The lanes already running end first, so that none outlives the drain.
                    failure = ExceptionDispatchInfo.Capture(exception);
                    looking = false;
                }
            }

            if (lanes.Count == 0)
            {
                break;
            }

            List<Task> waits = [.. lanes.Values];
            if (looking)
            {
                waits.Add(wake);
            }

            await Task.WhenAny(waits);

            foreach (var (handler, lane) in lanes.Where(lane => lane.Value.IsCompleted).ToList())
            {
                lanes.Remove(handler);
                try
                {
                    delivered += await lane;
                }
                catch (Exception exception)
                {
                    failure ??= ExceptionDispatchInfo.Capture(exception);
                }
            }
        }

        failure?.Throw();
        return delivered;
    }

    // Leaves the rows whose attempts are used up and whose worker is gone as failed, and says whether any row is due to be
    // delivered. Each is read first, so that an idle outbox takes no write lock.
    public async Task<bool> SweepAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        var now = time.GetUtcNow();

        if (await Exhausted(context, now).AnyAsync(cancellationToken))
        {
            var exhausted = await Exhausted(context, now).ExecuteUpdateAsync(
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
        }

        return await Claimable(context, now).AnyAsync(cancellationToken);
    }

    private async Task<List<string>> DueHandlersAsync(ConcurrentDictionary<Guid, byte> deferred, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        var skipped = deferred.Keys.ToList();

        return await Claimable(context, time.GetUtcNow())
            .Where(message => !skipped.Contains(message.Id))
            .Select(message => message.Handler)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    private async Task<int> LaneAsync(string handler, ConcurrentDictionary<Guid, byte> deferred, CancellationToken cancellationToken)
    {
        var delivered = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var claim = await ClaimAsync(handler, deferred, cancellationToken);
            if (claim.Rows.Count == 0)
            {
                break;
            }

            delivered += await DeliverBatchAsync(claim, deferred, cancellationToken);
        }

        return delivered;
    }

    private async Task<Claim> ClaimAsync(string handler, ConcurrentDictionary<Guid, byte> deferred, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();

        var now = time.GetUtcNow();
        var leaseUntil = now + options.LeaseDuration;
        var owner = $"{_instance}/{Interlocked.Increment(ref _claims)}";
        var skipped = deferred.Keys.ToList();

        var candidates = await Claimable(context, now)
            .Where(message => message.Handler == handler && !skipped.Contains(message.Id))
            .OrderBy(message => message.Sequence).ThenBy(message => message.Id)
            .Select(message => new { message.Id, message.LeaseOwner })
            .Take(options.BatchSize)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return new Claim(owner, leaseUntil, []);
        }

        // A row that still names an owner was held by a worker whose lease lapsed, which its delivery may have killed.
        var ids = candidates.TakeWhile(candidate => candidate.LeaseOwner is null).Select(candidate => candidate.Id).ToList();
        if (ids.Count == 0)
        {
            ids = [candidates[0].Id];
        }

        // The update checks every condition of the read again, so a row another drain took in between is left to it.
        var won = await Claimable(context, now).Where(message => ids.Contains(message.Id)).ExecuteUpdateAsync(
            message => message
                .SetProperty(row => row.LeaseOwner, owner)
                .SetProperty(row => row.LeaseUntil, leaseUntil)
                .SetProperty(row => row.Attempts, row => row.Attempts + 1),
            cancellationToken);

        if (won == 0)
        {
            return new Claim(owner, leaseUntil, []);
        }

        var rows = await context.Set<OutboxMessage>().AsNoTracking()
            .Where(message => ids.Contains(message.Id) && message.Status == OutboxStatus.Pending && message.LeaseOwner == owner)
            .OrderBy(message => message.Sequence).ThenBy(message => message.Id)
            .ToListAsync(cancellationToken);

        return new Claim(owner, leaseUntil, rows);
    }

    private async Task<int> DeliverBatchAsync(Claim claim, ConcurrentDictionary<Guid, byte> deferred, CancellationToken cancellationToken)
    {
        var delivered = 0;
        List<OutboxMessage> done = [];
        HashSet<Guid> lost = [];
        var margin = (options.LeaseDuration - options.DeliveryTimeout) / 2;

        for (var index = 0; index < claim.Rows.Count; index++)
        {
            var message = claim.Rows[index];
            if (cancellationToken.IsCancellationRequested)
            {
                await ReleaseAsync(message, delay: null);
                continue;
            }

            if (claim.LeaseUntil - time.GetUtcNow() < options.DeliveryTimeout + margin)
            {
                await SettleDeliveredAsync(claim, done);
                lost.UnionWith(await RenewAsync(claim, claim.Rows.Skip(index).Select(row => row.Id).ToList()));
            }

            if (lost.Contains(message.Id))
            {
                EventLog.LeaseLost(logger, message.Id, message.Handler);
                continue;
            }

            if (await DeliverAsync(message, deferred, cancellationToken))
            {
                delivered++;
                done.Add(message);
            }
        }

        await SettleDeliveredAsync(claim, done);
        return delivered;
    }

    // Renews the lease of the rows left in a batch, and gives back the ids of those this worker no longer holds.
    private async Task<List<Guid>> RenewAsync(Claim claim, List<Guid> ids)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
            var until = time.GetUtcNow() + options.LeaseDuration;
            var renewed = await HeldBy(context, claim, ids).ExecuteUpdateAsync(row => row.SetProperty(column => column.LeaseUntil, until));
            claim.LeaseUntil = until;
            if (renewed == ids.Count)
            {
                return [];
            }

            var held = await HeldBy(context, claim, ids).Select(row => row.Id).ToListAsync();
            return [.. ids.Except(held)];
        }
        catch (Exception exception)
        {
            // Nothing is delivered on a lease that may have run out; the rows go to a later drain when it does.
            EventLog.SettleFailed(logger, exception, ids[0]);
            return ids;
        }
    }

    // One update for the rows delivered so far. A write that fails leaves them leased, so they are delivered again when the
    // lease ends.
    private async Task SettleDeliveredAsync(Claim claim, List<OutboxMessage> done)
    {
        if (done.Count == 0)
        {
            return;
        }

        var ids = done.Select(message => message.Id).ToList();
        done.Clear();
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
            var now = time.GetUtcNow();
            await HeldBy(context, claim, ids).ExecuteUpdateAsync(row => row
                .SetProperty(column => column.Status, OutboxStatus.Delivered)
                .SetProperty(column => column.DeliveredAt, now)
                .SetProperty(column => column.LastError, (string?)null)
                .SetProperty(column => column.LeaseOwner, (string?)null)
                .SetProperty(column => column.LeaseUntil, (DateTimeOffset?)null));
        }
        catch (Exception exception)
        {
            foreach (var id in ids)
            {
                EventLog.SettleFailed(logger, exception, id);
            }
        }
    }

    private static IQueryable<OutboxMessage> Open(SystemDbContext context, DateTimeOffset now) =>
        context.Set<OutboxMessage>().Where(message =>
            message.Status == OutboxStatus.Pending
            && message.AvailableAt <= now
            && (message.LeaseUntil == null || message.LeaseUntil <= now));

    private IQueryable<OutboxMessage> Claimable(SystemDbContext context, DateTimeOffset now)
    {
        var limit = options.MaxAttempts;
        return Open(context, now).Where(message => message.Attempts < limit);
    }

    private IQueryable<OutboxMessage> Exhausted(SystemDbContext context, DateTimeOffset now)
    {
        var limit = options.MaxAttempts;
        return Open(context, now).Where(message => message.Attempts >= limit);
    }

    private static IQueryable<OutboxMessage> Held(SystemDbContext context, OutboxMessage message) =>
        context.Set<OutboxMessage>().Where(row =>
            row.Id == message.Id && row.Status == OutboxStatus.Pending && row.LeaseOwner == message.LeaseOwner && row.Attempts == message.Attempts);

    private static IQueryable<OutboxMessage> HeldBy(SystemDbContext context, Claim claim, List<Guid> ids) =>
        context.Set<OutboxMessage>().Where(row => ids.Contains(row.Id) && row.Status == OutboxStatus.Pending && row.LeaseOwner == claim.Owner);

    // True when the handler succeeded; the delivery is then settled with the rest of its batch.
    private async Task<bool> DeliverAsync(OutboxMessage message, ConcurrentDictionary<Guid, byte> deferred, CancellationToken cancellationToken)
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
            await ReleaseAsync(message, options.MissingHandlerDelay);
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
            deferred.TryAdd(message.Id, 0);
            await ReleaseAsync(message, delay: null);
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
            await ReleaseAsync(message, options.MissingHandlerDelay);
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
            await ReleaseAsync(message, delay: null);
            return false;
        }
        catch (TimeoutException)
        {
            // Read here and not in a filter, which would run before the finally that abandons the hand-off.
            if (state.WasAbandoned)
            {
                EventLog.HandOffAbandoned(logger, message.Id, message.Handler, options.DeliveryTimeout);
                deferred.TryAdd(message.Id, 0);
                await ReleaseAsync(message, delay: null);
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

        return true;
    }

    private async Task FailAsync(OutboxMessage message, string error)
    {
        if (message.Attempts >= options.MaxAttempts)
        {
            EventLog.DeliveryGaveUp(logger, message.Id, message.Handler, message.Attempts, error);
            await SettleFailedAsync(message, error, back: 0);
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

    // Moved to the dead-letter list with the attempt of its claim given back, since no handler ran.
    private Task OrphanAsync(OutboxMessage message, string error)
    {
        EventLog.Orphaned(logger, message.Id, message.Handler, options.OrphanAfter);
        return SettleFailedAsync(message, error, back: 1);
    }

    // A failed row is never available again, so its available_at says when it failed, which FailedRetention counts from.
    private Task SettleFailedAsync(OutboxMessage message, string error, int back)
    {
        var now = time.GetUtcNow();
        return SettleAsync(message, row => row
            .SetProperty(column => column.Status, OutboxStatus.Failed)
            .SetProperty(column => column.Attempts, column => column.Attempts - back)
            .SetProperty(column => column.LastError, error)
            .SetProperty(column => column.AvailableAt, now)
            .SetProperty(column => column.LeaseOwner, (string?)null)
            .SetProperty(column => column.LeaseUntil, (DateTimeOffset?)null));
    }

    // The attempt of the claim is given back, since the handler did not run to an end.
    private Task ReleaseAsync(OutboxMessage message, TimeSpan? delay)
    {
        var availableAt = delay is { } wait ? time.GetUtcNow() + wait : message.AvailableAt;
        return SettleAsync(message, row => row
            .SetProperty(column => column.Attempts, column => column.Attempts - 1)
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

        // Its token is cancelled here, before its delivery is settled, since the timer of its own limit can fire after the
        // cut-off on a busy machine.
        var told = attempt.CancelAsync();
        _ = Task.WhenAll(run, told).ContinueWith(
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

    private sealed class Claim(string owner, DateTimeOffset leaseUntil, List<OutboxMessage> rows)
    {
        public string Owner { get; } = owner;

        public DateTimeOffset LeaseUntil { get; set; } = leaseUntil;

        public List<OutboxMessage> Rows { get; } = rows;
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
