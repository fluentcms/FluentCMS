using System.Data.Common;
using FluentCMS.Content;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Plugins;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Events;

// Delivers the rows of cms_outbox at least once. Each owner of handlers, a plugin or core, has a lane of its own, which takes
// the rows of its handlers in the order of their events, so one owner gets its events in sequence on this instance whatever
// their types, and an owner whose handler is slow or hangs holds back only its own deliveries. That order holds per instance:
// another instance's drain may claim the owner's next batch at the same time, and a row that fails waits for its retry while
// later rows go on. Rows of a handler that this instance does not have share one lane more. The drain starts a lane for each
// owner with rows due, and looks again, at the owners with no lane only, whenever a lane ends or a kick of this instance
// signals it, so that a new event for an idle owner does not wait for a slow one. There are as many lanes as owners, a few.
// A lane that fails, as on a locked database, is logged and looked at again on the next signal, while the others go on.
// A lane claims a batch with one conditional update over its ids, which only one of any number of instances wins for each
// row, and holds a lease under an owner of that claim alone, so that two drains of one instance never take each other's rows
// when they read back what they won. The candidates are read handler by handler, each from its own place in the pending rows,
// and merged in the order of their events, so that the claim of one owner never walks the backlog of another.
// The claim counts an attempt for each row it takes, so that a delivery that kills its process uses up its attempts with no
// write of its own; a row that is not run to an end gives its attempt back. A row taken over from a worker of another process
// whose lease lapsed is claimed alone, so that a delivery that kills its process costs the rows after it one attempt at most;
// one that a claim of this instance gave up without a write, as when a release failed, was not killed by its delivery, so it
// is claimed with the rest of its batch. A claim that fails or is stopped after its update gives back what it took. The lease
// of a batch is renewed only when what is left of it would not cover one more delivery, with the rows given back so far. The
// rows delivered are settled together then and at the end of the batch, and the rows given back with one write at the end, so
// a batch whose handlers answer in time costs one write to claim it and one to settle it; a renewal that fails ends the batch
// and gives back the rows not tried, and so does a stopping host. The writes of a stop have the job runner's ReleaseTimeout
// in all: those before the release, the settle first, have half at most, and the release what is left. What does not fit
// stays leased. A crash before the settle delivers the batch again. A delivery is cut off before its lease can end, so that a
// worker never delivers on a lease it has lost, and every write of an outcome is conditional on the claim. No handler starts
// once its lane is told of a stop.
// A cut-off that comes before the handler starts, as when the thread pool is starved, gives the attempt back and leaves the
// row out of this drain for one MissingHandlerDelay. A handler that has to be abandoned once it has started may still be running,
// on a thread of its own; nothing here can stop it.
// A row of a plugin that is now disabled in the row's site was recorded while it was enabled, so it is owed to the plugin: it
// waits, as a row with no handler does, without using an attempt, and is delivered once the plugin is enabled there again.
// Either waits OrphanAfter at most, from when its event happened; then it is left as failed, so that a plugin that is gone
// for good leaves rows in the dead-letter list rather than rows claimed again for ever. When which plugins are disabled
// cannot be read, the batch is given back with one write and left out of this drain for one MissingHandlerDelay, and its
// lane waits for the next signal, so that the read is not tried again for each row.
internal sealed class OutboxDrainer(
    IServiceScopeFactory scopes,
    OutboxRegistry registry,
    PluginState plugins,
    OutboxOptions options,
    JobRunnerOptions jobs,
    IOperationContextAccessor operation,
    TimeProvider time,
    ILogger<OutboxDrainer> logger)
{
    private const string ExhaustedError = "The worker of the last attempt stopped without finishing it, and no attempts are left.";

    private const string Unregistered = Lanes.Unregistered;

    private readonly string _instance = $"{Environment.MachineName}/{Guid.NewGuid():N}";

    private static readonly TimeSpan[] BusyRetries = [TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(1)];

    private long _claims;

    private int _draining;

    private TaskCompletionSource _wake = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // On a thread of its own, since a handler that blocks before its first await would never hand control back to the
    // cut-off. A test replaces it to hold a handler back past its cut-off.
    internal Func<Func<Task>, Task> HandOff { get; set; } = work => Task.Run(work);

    // The owner of a handler: its plugin, or null for core's and the host's. A test replaces it to give handlers owners.
    internal Func<string, string?> OwnerOf { get; set; } = plugins.OutboxHandlerOwner;

    // Asks the drains running on this instance to look again for owners with rows due that have no lane yet.
    public void Signal() =>
        Interlocked.Exchange(ref _wake, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();

    // The number of deliveries that succeeded.
    public async Task<int> DrainAsync(CancellationToken cancellationToken = default) => (await DrainLanesAsync(cancellationToken)).Delivered;

    // With the lanes still running when the drain returned at the end of a stop, whose writes a provider may not have let go.
    private async Task<(int Delivered, Task Unfinished)> DrainLanesAsync(CancellationToken cancellationToken)
    {
        var lanesOf = new Lanes(registry.Names, OwnerOf);
        var delivered = 0;
        var deferred = new Deferrals(time, options.MissingHandlerDelay, options.BatchSize);
        var running = new Dictionary<string, Task<(int Delivered, bool Stopped)>>(StringComparer.Ordinal);

        // A lane that failed is left out until the next signal, so that a database that refuses it is not asked again at once.
        var failed = new Dictionary<string, Task>(StringComparer.Ordinal);
        var first = true;

        // The drain's own, whose end no provider is given, so that none can hold it back by ignoring it or in its callback.
        await using var stop = new Stop(jobs.ReleaseTimeout, time, cancellationToken);
        var outOfTime = Task.Delay(Timeout.InfiniteTimeSpan, stop.Ended);

        while (!stop.Ended.IsCancellationRequested)
        {
            var wake = Volatile.Read(ref _wake).Task;
            if (!cancellationToken.IsCancellationRequested)
            {
                var idle = lanesOf.Keys.Where(key => !running.ContainsKey(key) && !(failed.TryGetValue(key, out var since) && since == wake)).ToList();
                try
                {
                    // The rows of handlers this instance does not have are looked for once a drain, since that walks the
                    // pending rows of every other handler.
                    var looking = Task.Run(() => DueLanesAsync(lanesOf, idle, first, deferred, failed, wake, stop.Requested), CancellationToken.None);
                    foreach (var key in await looking.WaitAsync(stop.Ended))
                    {
                        failed.Remove(key);
                        running[key] = Task.Run(() => LaneAsync(lanesOf, key, deferred, cancellationToken), CancellationToken.None);
                    }

                    first = false;
                }
                catch (Exception exception)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        LaneFailed(exception, null);
                    }

                    if (running.Count == 0)
                    {
                        break;
                    }
                }
            }

            if (running.Count == 0)
            {
                break;
            }

            List<Task> waits = [.. running.Values, outOfTime];
            if (!cancellationToken.IsCancellationRequested)
            {
                waits.Add(wake);
            }

            await Task.WhenAny(waits);

            foreach (var (key, lane) in running.Where(lane => lane.Value.IsCompleted).ToList())
            {
                running.Remove(key);
                try
                {
                    var (count, stopped) = await lane;
                    delivered += count;
                    if (stopped)
                    {
                        failed[key] = Volatile.Read(ref _wake).Task;
                    }
                }
                catch (Exception exception)
                {
                    // Every lane that ends in time is observed, whatever a stopping host made its provider throw.
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        LaneFailed(exception, key);
                        failed[key] = Volatile.Read(ref _wake).Task;
                    }
                }
            }
        }

        return (delivered, running.Count == 0 ? Task.CompletedTask : Task.WhenAll(running.Values));
    }

    // Runs a drain unless one already runs on this instance, which is then asked to look again, so that an outbox queue with
    // more than one worker still gives one lane to each owner here and keeps the order of its events. A drain cancelled
    // without a host stop, as when its job lost its lease, returns at the end of ReleaseTimeout while a lane may still wait for
    // its release; it holds off the next drain until its lanes end, so that no new lane claims an owner's later rows while
    // its earlier ones are still leased. Those wait for the next kick or sweep.
    public async Task<int> DrainOnceAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _draining, 1, 0) != 0)
        {
            Signal();
            return 0;
        }

        var unfinished = Task.CompletedTask;
        try
        {
            (var delivered, unfinished) = await DrainLanesAsync(cancellationToken);
            return delivered;
        }
        finally
        {
            _ = unfinished.ContinueWith(
                ended =>
                {
                    _ = ended.Exception;
                    Volatile.Write(ref _draining, 0);
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    // SQLite takes one writer at a time, so lanes that claim together find it busy; that is retried in the lane and, when it
    // lasts, logged below an error.
    private void LaneFailed(Exception exception, string? key)
    {
        var owner = key is null ? "(looking for owners)" : Lanes.Display(key);
        if (IsBusy(exception))
        {
            EventLog.LaneBusy(logger, exception, owner);
        }
        else
        {
            EventLog.LaneFailed(logger, exception, owner);
        }
    }

    private static bool IsBusy(Exception exception) => exception is DbException { IsTransient: true } || exception.InnerException is DbException { IsTransient: true };

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

    // Only the lanes that are idle are looked at, each with a seek per handler, so that a burst of signals while a slow lane
    // runs costs a few reads each and never a walk of its backlog. A lane whose read fails is left out until the next signal.
    private async Task<List<string>> DueLanesAsync(Lanes lanesOf, List<string> idle, bool unregistered, Deferrals deferred, Dictionary<string, Task> failed, Task wake, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        var now = time.GetUtcNow();
        var skipped = deferred.Current();
        List<string> due = [];

        foreach (var key in idle)
        {
            try
            {
                if (await IsDueAsync(context, lanesOf, key, unregistered, now, skipped, cancellationToken))
                {
                    due.Add(key);
                }
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                LaneFailed(exception, key);
                failed[key] = wake;
            }
        }

        return due;
    }

    private async Task<bool> IsDueAsync(SystemDbContext context, Lanes lanesOf, string key, bool unregistered, DateTimeOffset now, List<Guid> skipped, CancellationToken cancellationToken)
    {
        if (key == Unregistered)
        {
            return unregistered && await OfUnregistered(context, lanesOf, now).Where(message => !skipped.Contains(message.Id)).AnyAsync(cancellationToken);
        }

        foreach (var handler in lanesOf.HandlersOf(key))
        {
            if (await Claimable(context, now).Where(message => message.Handler == handler && !skipped.Contains(message.Id)).AnyAsync(cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private IQueryable<OutboxMessage> OfUnregistered(SystemDbContext context, Lanes lanesOf, DateTimeOffset now)
    {
        var known = lanesOf.Known;
        return Claimable(context, now).Where(message => !known.Contains(message.Handler));
    }

    // Stopped when a renewal failed or the plugin state could not be read, so that the lane waits for the next signal rather
    // than claim what it just gave back.
    private async Task<(int Delivered, bool Stopped)> LaneAsync(Lanes lanesOf, string key, Deferrals deferred, CancellationToken hostStopping)
    {
        await using var stop = new Stop(jobs.ReleaseTimeout, time, hostStopping);
        var cancellationToken = stop.Requested;
        var delivered = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var claim = await ClaimWithRetryAsync(lanesOf, key, deferred, stop);
            if (claim.Rows.Count == 0)
            {
                break;
            }

            var (count, stopped) = await DeliverBatchAsync(claim, deferred, stop);
            delivered += count;
            if (stopped)
            {
                return (delivered, true);
            }
        }

        return (delivered, false);
    }

    private async Task<Claim> ClaimWithRetryAsync(Lanes lanesOf, string key, Deferrals deferred, Stop stop)
    {
        var cancellationToken = stop.Requested;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await ClaimAsync(lanesOf, key, deferred, stop);
            }
            catch (Exception exception) when (attempt < BusyRetries.Length + 1 && IsBusy(exception) && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(BusyRetries[attempt - 1], time, cancellationToken);
            }
        }
    }

    private async Task<Claim> ClaimAsync(Lanes lanesOf, string key, Deferrals deferred, Stop stop)
    {
        var cancellationToken = stop.Requested;
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();

        var now = time.GetUtcNow();
        var leaseUntil = now + options.LeaseDuration;
        var owner = $"{_instance}/{Interlocked.Increment(ref _claims)}";
        var skipped = deferred.Current();

        List<Candidate> candidates = [];
        if (key == Unregistered)
        {
            candidates.AddRange(await Candidates(OfUnregistered(context, lanesOf, now), skipped).ToListAsync(cancellationToken));
        }
        else
        {
            foreach (var handler in lanesOf.HandlersOf(key))
            {
                candidates.AddRange(await CandidatesOf(context, handler, now, skipped).ToListAsync(cancellationToken));
            }
        }

        if (candidates.Count == 0)
        {
            return new Claim(owner, leaseUntil, []);
        }

        var ordered = candidates.OrderBy(candidate => candidate.Sequence).ThenBy(candidate => candidate.Id).Take(options.BatchSize).ToList();

        // A row that still names an owner was held by a worker whose lease lapsed, which its delivery may have killed, unless
        // that worker is a claim of this instance, which is still running.
        var ids = ordered.TakeWhile(candidate => candidate.LeaseOwner is null || IsOwn(candidate.LeaseOwner)).Select(candidate => candidate.Id).ToList();
        if (ids.Count == 0)
        {
            ids = [ordered[0].Id];
        }

        // The update checks every condition of the read again, so a row another drain took in between is left to it.
        var won = 0;
        try
        {
            won = await Claimable(context, now).Where(message => ids.Contains(message.Id)).ExecuteUpdateAsync(
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
        catch (Exception) when (won > 0 || cancellationToken.IsCancellationRequested)
        {
            // The update may have landed though nothing of it was read back, let alone delivered, so what it took goes back
            // with the attempt it counted; the owner is this claim's alone, so a release of what it did not take matches nothing.
            await ReleaseAsync(new Claim(owner, leaseUntil, []), ids, [], stop.Ended);
            throw;
        }
    }

    // The first rows of one handler that a claim may take, in event order; ix_cms_outbox_handler_pending serves it.
    internal IQueryable<Candidate> CandidatesOf(SystemDbContext context, string handler, DateTimeOffset now, List<Guid> skipped) =>
        Candidates(Claimable(context, now).Where(message => message.Handler == handler), skipped);

    private IQueryable<Candidate> Candidates(IQueryable<OutboxMessage> rows, List<Guid> skipped) =>
        rows.Where(message => !skipped.Contains(message.Id))
            .OrderBy(message => message.Sequence).ThenBy(message => message.Id)
            .Take(options.BatchSize)
            .Select(message => new Candidate(message.Id, message.Sequence, message.LeaseOwner));

    // The owner of every claim of this instance starts with its name.
    private bool IsOwn(string owner) => owner.StartsWith($"{_instance}/", StringComparison.Ordinal);

    private async Task<(int Delivered, bool Stopped)> DeliverBatchAsync(Claim claim, Deferrals deferred, Stop stop)
    {
        var cancellationToken = stop.Requested;
        var delivered = 0;
        var outcomes = new Outcomes(claim, stop);
        HashSet<Guid> lost = [];
        var margin = (options.LeaseDuration - options.DeliveryTimeout) / 2;

        for (var index = 0; index < claim.Rows.Count; index++)
        {
            var message = claim.Rows[index];
            if (cancellationToken.IsCancellationRequested)
            {
                outcomes.Back.AddRange(claim.Rows.Skip(index).Where(row => !lost.Contains(row.Id)));
                break;
            }

            if (claim.LeaseUntil - time.GetUtcNow() < options.DeliveryTimeout + margin)
            {
                await SettleDeliveredAsync(outcomes);
                var left = claim.Rows.Skip(index).ToList();
                if (await RenewAsync(claim, left, outcomes, stop.SettleEnded) is not { } gone)
                {
                    // Nothing more is delivered on a lease that may not be held; the rows not tried go back for their attempt.
                    outcomes.Back.AddRange(left);
                    await ReleaseAsync(outcomes);
                    return (delivered, true);
                }

                lost.UnionWith(gone);
            }

            if (lost.Contains(message.Id))
            {
                EventLog.LeaseLost(logger, message.Id, message.Handler);
                continue;
            }

            switch (await DeliverAsync(message, outcomes, deferred, cancellationToken))
            {
                case Outcome.Delivered:
                    delivered++;
                    outcomes.Done.Add(message);
                    break;
                case Outcome.GivenBack:
                    outcomes.Back.Add(message);
                    break;
                case Outcome.Waits:
                    outcomes.Waiting.Add(message);
                    break;
                case Outcome.Settled:
                    break;
                case Outcome.StateUnavailable:
                    // The rest of the batch would fail the same read, so it goes back with this row, and the lane waits.
                    var rest = claim.Rows.Skip(index).Where(row => !lost.Contains(row.Id)).ToList();
                    rest.ForEach(row => deferred.Add(row.Id));
                    outcomes.Back.AddRange(rest);
                    await SettleBatchAsync(outcomes);
                    return (delivered, true);
            }
        }

        await SettleBatchAsync(outcomes);
        return (delivered, false);
    }

    // Renews the lease of the rows left in a batch, and of those given back so far, which go with the one release of the batch
    // at its end, and gives back the ids of those this worker no longer holds, or null when the renewal failed.
    private async Task<List<Guid>?> RenewAsync(Claim claim, List<OutboxMessage> left, Outcomes outcomes, CancellationToken limit)
    {
        var ids = left.Concat(outcomes.Back).Concat(outcomes.Waiting).Select(row => row.Id).ToList();
        try
        {
            var until = time.GetUtcNow() + options.LeaseDuration;
            var held = await InTimeAsync(
                async (context, token) =>
                {
                    var renewed = await HeldBy(context, claim, ids).ExecuteUpdateAsync(row => row.SetProperty(column => column.LeaseUntil, until), token);
                    return renewed == ids.Count ? ids : await HeldBy(context, claim, ids).Select(row => row.Id).ToListAsync(token);
                },
                limit);
            claim.LeaseUntil = until;
            return [.. ids.Except(held)];
        }
        catch (Exception exception)
        {
            EventLog.RenewFailed(logger, exception, left[0].Handler, left.Count);
            return null;
        }
    }

    // The rows of a batch that ran to an end without a write of their own: one update for those delivered, and one for those
    // given back.
    private async Task SettleBatchAsync(Outcomes outcomes)
    {
        await SettleDeliveredAsync(outcomes);
        await ReleaseAsync(outcomes);
    }

    // One write for every row of the batch given back, so that a stop's release is never split into a first write that uses
    // up the time of the next.
    private async Task ReleaseAsync(Outcomes outcomes)
    {
        await ReleaseAsync(outcomes.Claim, [.. outcomes.Back.Select(row => row.Id)], [.. outcomes.Waiting.Select(row => row.Id)], outcomes.Stop.Ended);
        outcomes.Back.Clear();
        outcomes.Waiting.Clear();
    }

    // One update for the rows delivered so far. A write that fails, or that outlasts the half of a stop, leaves them leased,
    // so they are delivered again when the lease ends, or failed by the sweep on their last attempt.
    private async Task SettleDeliveredAsync(Outcomes outcomes)
    {
        if (outcomes.Done.Count == 0)
        {
            return;
        }

        var ids = outcomes.Done.Select(message => message.Id).ToList();
        outcomes.Done.Clear();
        try
        {
            var now = time.GetUtcNow();
            await InTimeAsync(
                (context, token) => HeldBy(context, outcomes.Claim, ids).ExecuteUpdateAsync(
                    row => row
                        .SetProperty(column => column.Status, OutboxStatus.Delivered)
                        .SetProperty(column => column.DeliveredAt, now)
                        .SetProperty(column => column.LastError, (string?)null)
                        .SetProperty(column => column.LeaseOwner, (string?)null)
                        .SetProperty(column => column.LeaseUntil, (DateTimeOffset?)null),
                    token),
                outcomes.Stop.SettleEnded);
        }
        catch (Exception exception)
        {
            foreach (var id in ids)
            {
                EventLog.SettleFailed(logger, exception, id);
            }
        }
    }

    // One update that gives the attempt of their claim back to rows whose handler did not run to an end: those that go back at
    // once keep their available_at, and those that wait have it moved by MissingHandlerDelay. A write that fails leaves them
    // leased; once the lease ends, this instance claims them with the rest of their batch. After a stop this instance is gone,
    // so the next one claims them one per claim, and its sweep fails those that were on their last attempt.
    private async Task ReleaseAsync(Claim claim, List<Guid> back, List<Guid> waiting, CancellationToken limit)
    {
        List<Guid> ids = [.. back, .. waiting];
        if (ids.Count == 0)
        {
            return;
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
            var at = time.GetUtcNow() + options.MissingHandlerDelay;
            await HeldBy(context, claim, ids).ExecuteUpdateAsync(
                row =>
                {
                    row.SetProperty(column => column.Attempts, column => column.Attempts - 1)
                        .SetProperty(column => column.LeaseOwner, (string?)null)
                        .SetProperty(column => column.LeaseUntil, (DateTimeOffset?)null);
                    if (back.Count == 0)
                    {
                        row.SetProperty(column => column.AvailableAt, at);
                    }
                    else if (waiting.Count > 0)
                    {
                        row.SetProperty(column => column.AvailableAt, column => waiting.Contains(column.Id) ? at : column.AvailableAt);
                    }
                },
                limit);
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

    // Delivered, given back or waiting, the delivery is settled with the rest of its batch; a failure is written on its own.
    private async Task<Outcome> DeliverAsync(OutboxMessage message, Outcomes outcomes, Deferrals deferred, CancellationToken cancellationToken)
    {
        var registration = registry.Find(message.Handler);
        if (registration is null)
        {
            // Another instance may have the handler: this one is mid-deploy, or the plugin is gone. The row waits, and a
            // plugin that stays removed leaves its rows failed once they are old enough.
            if (Orphaned(message))
            {
                await OrphanAsync(message, outcomes, $"No handler is registered as '{message.Handler}', and the delivery waited longer than {options.OrphanAfter} for one.");
                return Outcome.Settled;
            }

            EventLog.HandlerMissing(logger, message.Id, message.Handler);
            return Outcome.Waits;
        }

        string? disabled;
        try
        {
            disabled = await plugins.DisabledOutboxHandlerAsync(message.Handler, message.SiteId, cancellationToken);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            // Whatever the provider threw for the stop, the row goes back, and the rest of its batch with it at the next check.
            return Outcome.GivenBack;
        }
        catch (Exception exception)
        {
            // Given back for a later drain with the rest of the batch, so that none of it is left leased.
            PluginLog.StateUnavailable(logger, exception);
            return Outcome.StateUnavailable;
        }

        if (disabled is not null)
        {
            if (Orphaned(message))
            {
                await OrphanAsync(message, outcomes, $"The handler '{message.Handler}' belongs to the plugin '{disabled}', which is disabled in the site {message.SiteId:D}, and the delivery waited longer than {options.OrphanAfter} for it to be enabled.");
                return Outcome.Settled;
            }

            PluginLog.DeliveryWaits(logger, message.Id, message.Handler, disabled, message.SiteId!.Value);
            return Outcome.Waits;
        }

        var state = new HandOffState();
        try
        {
            var @event = registration.Read(message.Payload)
                ?? throw new InvalidOperationException($"The payload of the event for '{message.Handler}' is null.");

            var scope = scopes.CreateAsyncScope();
            var limit = new CancellationTokenSource(options.DeliveryTimeout, time);
            var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, limit.Token);
            Task<OperationContext>? creating = null;
            Task? run = null;

            var previous = operation.Current;
            try
            {
                // The read of the site's locale is shared and takes no token, so a stop does not wait for it.
                creating = EventOperation.CreateAsync(scope.ServiceProvider, @event.Source, message.EventId, message.SiteId, time.GetUtcNow());
                operation.Current = await creating.WaitAsync(cancellationToken);
                var delivery = new EventDelivery(message.Id, message.Attempts);
                run = HandOff(() => state.TryStart(cancellationToken) ? registration.Invoke(scope.ServiceProvider, @event, delivery, attempt.Token).AsTask() : Task.CompletedTask);
                await run.WaitAsync(options.DeliveryTimeout, time, cancellationToken);

                // Abandoned before it could start only for a stop, which gives the row back; it was never delivered.
                if (state.WasAbandoned)
                {
                    return Outcome.GivenBack;
                }
            }
            finally
            {
                operation.Current = previous;

                // So that a handler which has not started by now never starts on the scope.
                state.Abandon();

                // A read the stop left still uses the scope, which goes once the read ends.
                var ending = run ?? creating?.ContinueWith(static read => _ = read.Exception, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                await EndAsync(ending, scope, limit, attempt, message, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Outcome.GivenBack;
        }
        catch (TimeoutException)
        {
            // Read here and not in a filter, which would run before the finally that abandons the hand-off.
            if (state.WasAbandoned)
            {
                EventLog.HandOffAbandoned(logger, message.Id, message.Handler, options.DeliveryTimeout);
                deferred.Add(message.Id);
                return Outcome.GivenBack;
            }

            EventLog.DeliveryAbandoned(logger, message.Id, message.Handler, options.DeliveryTimeout);
            await FailAsync(message, outcomes, $"The handler did not finish within {options.DeliveryTimeout}.");
            return Outcome.Settled;
        }
        catch (Exception exception)
        {
            await FailAsync(message, outcomes, OutboxErrorText.Of(exception, message.Payload));
            return Outcome.Settled;
        }

        return Outcome.Delivered;
    }

    private async Task FailAsync(OutboxMessage message, Outcomes outcomes, string error)
    {
        if (message.Attempts >= options.MaxAttempts)
        {
            EventLog.DeliveryGaveUp(logger, message.Id, message.Handler, message.Attempts, error);
            await SettleFailedAsync(message, outcomes, error, back: 0);
            return;
        }

        EventLog.DeliveryFailed(logger, message.Id, message.Handler, message.Attempts, error);
        var retryAt = time.GetUtcNow() + options.RetryAfter(message.Attempts);
        await SettleAsync(message, outcomes, row => row
            .SetProperty(column => column.AvailableAt, retryAt)
            .SetProperty(column => column.LastError, error)
            .SetProperty(column => column.LeaseOwner, (string?)null)
            .SetProperty(column => column.LeaseUntil, (DateTimeOffset?)null));
    }

    private bool Orphaned(OutboxMessage message) => message.OccurredAt <= time.GetUtcNow() - options.OrphanAfter;

    // Moved to the dead-letter list with the attempt of its claim given back, since no handler ran.
    private Task OrphanAsync(OutboxMessage message, Outcomes outcomes, string error)
    {
        EventLog.Orphaned(logger, message.Id, message.Handler, options.OrphanAfter);
        return SettleFailedAsync(message, outcomes, error, back: 1);
    }

    // A failed row is never available again, so its available_at says when it failed, which FailedRetention counts from.
    private Task SettleFailedAsync(OutboxMessage message, Outcomes outcomes, string error, int back)
    {
        var now = time.GetUtcNow();
        return SettleAsync(message, outcomes, row => row
            .SetProperty(column => column.Status, OutboxStatus.Failed)
            .SetProperty(column => column.Attempts, column => column.Attempts - back)
            .SetProperty(column => column.LastError, error)
            .SetProperty(column => column.AvailableAt, now)
            .SetProperty(column => column.LeaseOwner, (string?)null)
            .SetProperty(column => column.LeaseUntil, (DateTimeOffset?)null));
    }

    // Matches nothing once the lease has gone to another instance. A write that fails leaves the row leased, so it is
    // delivered again when the lease ends. A stop gives it up until the delivered rows of the batch are settled, so that it
    // cannot use up the time of their write, and then it has what is left of their half; written twice, it matches nothing
    // the second time.
    private async Task SettleAsync(OutboxMessage message, Outcomes outcomes, Action<UpdateSettersBuilder<OutboxMessage>> update)
    {
        try
        {
            try
            {
                await WriteAsync(outcomes.Stop.Requested);
            }
            catch (Exception) when (outcomes.Stop.Requested.IsCancellationRequested)
            {
                await SettleDeliveredAsync(outcomes);
                await WriteAsync(outcomes.Stop.SettleEnded);
            }
        }
        catch (Exception exception)
        {
            EventLog.SettleFailed(logger, exception, message.Id);
        }

        Task<int> WriteAsync(CancellationToken limit) =>
            InTimeAsync((context, token) => Held(context, message).ExecuteUpdateAsync(update, token), limit);
    }

    // For the writes that come before a release, which are not waited for past their limit, so that a provider that ignores it
    // or is slow to cancel leaves the releases the rest. One given up is cancelled, and is harmless when it still lands, since
    // it is conditional on the claim.
    private async Task<T> InTimeAsync<T>(Func<SystemDbContext, CancellationToken, Task<T>> write, CancellationToken limit)
    {
        var givenUp = new CancellationTokenSource();
        var run = Task.Run(
            async () =>
            {
                await using var scope = scopes.CreateAsyncScope();
                return await write(scope.ServiceProvider.GetRequiredService<SystemDbContext>(), givenUp.Token);
            },
            limit);
        try
        {
            return await run.WaitAsync(limit);
        }
        finally
        {
            if (!run.IsCompleted)
            {
                _ = givenUp.CancelAsync();
            }
        }
    }

    // A handler that was cut off, or left by a stopping host, may still be running on its scope and its token, so they go only
    // once it ends. What it throws then comes after its delivery was settled, and is only logged.
    private async ValueTask EndAsync(Task? run, AsyncServiceScope scope, CancellationTokenSource limit, CancellationTokenSource attempt, OutboxMessage message, CancellationToken stopping)
    {
        if (run is not { IsCompleted: false })
        {
            // Observed, in case it faulted after its cut-off.
            _ = run?.Exception;

            // A stop does not wait for a scope that is slow to go, so that what was delivered is settled in its time.
            await DisposeAsync(scope, limit, attempt, message).AsTask().WaitAsync(stopping).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
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

    internal sealed record Candidate(Guid Id, long Sequence, string? LeaseOwner);

    // The lanes of this instance: one for each owner of the handlers it has, and one for the rows of handlers it does not.
    private sealed class Lanes
    {
        // A plugin's name never has a star, so neither key is one.
        public const string Unregistered = "*unregistered";

        private const string Core = "*core";

        private readonly Dictionary<string, List<string>> _handlers = new(StringComparer.Ordinal);

        public Lanes(IEnumerable<string> names, Func<string, string?> ownerOf)
        {
            Known = [.. names];
            foreach (var name in Known.Order(StringComparer.Ordinal))
            {
                var key = ownerOf(name) ?? Core;
                if (!_handlers.TryGetValue(key, out var handlers))
                {
                    _handlers[key] = handlers = [];
                }

                handlers.Add(name);
            }

            Keys = [.. _handlers.Keys, Unregistered];
        }

        public List<string> Known { get; }

        public IReadOnlyList<string> Keys { get; }

        public static string Display(string key) => key switch
        {
            Unregistered => "(unregistered handlers)",
            Core => "(core)",
            _ => key,
        };

        public List<string> HandlersOf(string key) => _handlers.GetValueOrDefault(key) ?? [];
    }

    // The rows whose handler did not start in time, or whose plugin state could not be read, left out of this drain for a while
    // so that a starved thread pool or a lost database does not keep one drain going, and tried again after it. Their ids go
    // into every claim as parameters, so only the latest are kept; one left out early is only tried again sooner.
    internal sealed class Deferrals(TimeProvider time, TimeSpan span, int limit)
    {
        private readonly Dictionary<Guid, DateTimeOffset> _until = [];

        public void Add(Guid id)
        {
            lock (_until)
            {
                _until[id] = time.GetUtcNow() + span;
                while (_until.Count > limit)
                {
                    _until.Remove(_until.MinBy(entry => entry.Value).Key);
                }
            }
        }

        public List<Guid> Current()
        {
            var now = time.GetUtcNow();
            lock (_until)
            {
                foreach (var (id, until) in _until)
                {
                    if (until <= now)
                    {
                        _until.Remove(id);
                    }
                }

                return [.. _until.Keys];
            }
        }
    }

    // What became of a delivery. Settled is a failure or an orphan, written on its own; Waits gives the attempt back for one
    // MissingHandlerDelay, GivenBack gives it back at once, and StateUnavailable gives back the rest of the batch with it.
    private enum Outcome
    {
        Delivered,
        Settled,
        GivenBack,
        Waits,
        StateUnavailable,
    }

    // The host's stop as a lane, or the drain itself, sees it: that it came, the end of the half of ReleaseTimeout for what is
    // written before the release, and the end of it all. Each lane has its own, and each token is cancelled on a thread of its
    // own: what waits on one goes on, and a provider's callback runs, inside the call that cancels it, which would hold the
    // host, another lane or a timer. The sources are not disposed, since that can come before those threads are done.
    private sealed class Stop : IAsyncDisposable
    {
        private readonly ITimer _settle;
        private readonly ITimer _end;
        private readonly CancellationTokenRegistration _host;

        public Stop(TimeSpan timeout, TimeProvider time, CancellationToken host)
        {
            var requested = new CancellationTokenSource();
            var settleEnded = new CancellationTokenSource();
            var ended = new CancellationTokenSource();
            Requested = requested.Token;
            SettleEnded = settleEnded.Token;
            Ended = ended.Token;

            _settle = time.CreateTimer(_ => settleEnded.CancelAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _end = time.CreateTimer(_ => ended.CancelAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _host = host.UnsafeRegister(
                _ =>
                {
                    _settle.Change(timeout / 2, Timeout.InfiniteTimeSpan);
                    _end.Change(timeout, Timeout.InfiniteTimeSpan);
                    requested.CancelAsync();
                },
                null);
        }

        public CancellationToken Requested { get; }

        public CancellationToken SettleEnded { get; }

        public CancellationToken Ended { get; }

        public async ValueTask DisposeAsync()
        {
            await _host.DisposeAsync();
            await _settle.DisposeAsync();
            await _end.DisposeAsync();
        }
    }

    // The rows of a batch to settle together, and the stop their writes are bounded by.
    private sealed class Outcomes(Claim claim, Stop stop)
    {
        public Claim Claim { get; } = claim;

        public Stop Stop { get; } = stop;

        public List<OutboxMessage> Done { get; } = [];

        public List<OutboxMessage> Back { get; } = [];

        public List<OutboxMessage> Waiting { get; } = [];
    }

    private sealed class Claim(string owner, DateTimeOffset leaseUntil, List<OutboxMessage> rows)
    {
        public string Owner { get; } = owner;

        public DateTimeOffset LeaseUntil { get; set; } = leaseUntil;

        public List<OutboxMessage> Rows { get; } = rows;
    }

    // Decides once between the handler starting and the hand-off being abandoned, so exactly one of them happens. A stop that
    // its lane was told of abandons it too: one that came during a renewal or just before the hand-off would otherwise race
    // the work item against the abandon of the delivery, and could start a handler after the drain returned.
    private sealed class HandOffState
    {
        private const int Pending = 0;
        private const int Started = 1;
        private const int Abandoned = 2;

        private int _state;

        public bool WasAbandoned => Volatile.Read(ref _state) == Abandoned;

        public bool TryStart(CancellationToken stopping)
        {
            if (stopping.IsCancellationRequested)
            {
                Abandon();
            }

            return Interlocked.CompareExchange(ref _state, Started, Pending) == Pending;
        }

        public void Abandon() => Interlocked.CompareExchange(ref _state, Abandoned, Pending);
    }
}
