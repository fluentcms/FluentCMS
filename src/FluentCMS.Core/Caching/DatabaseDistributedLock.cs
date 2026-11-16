using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Caching;

// The default IDistributedLock, on cms_lock. A holder has a lease that it renews while it holds the lock, so one that
// stopped without releasing it blocks the others until the lease runs out. Every write is conditional on the row as the
// writer expects it, so of two that try at once one wins; a wait reads the row and writes nothing while it is held.
internal sealed class DatabaseDistributedLock(IServiceScopeFactory scopes, TimeProvider time, ILogger<DatabaseDistributedLock> logger) : IDistributedLock
{
    private static readonly TimeSpan FirstPoll = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan LongestPoll = TimeSpan.FromSeconds(1);

    // Who holds a lock, for whoever reads cms_lock; with a GUID per acquisition it stays within the owner column.
    private readonly string _instance = $"{Environment.MachineName[..Math.Min(Environment.MachineName.Length, 64)]}/{Environment.ProcessId}";

    public TimeSpan Lease { get; init; } = TimeSpan.FromSeconds(30);

    public async Task<IDistributedLockHandle?> TryAcquireAsync(string name, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(name.Length, LockRow.NameMaxLength, nameof(name));
        if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "The timeout is zero or more, or Timeout.InfiniteTimeSpan.");
        }

        var holder = $"{_instance}/{Guid.NewGuid():N}";
        var started = time.GetTimestamp();
        var poll = FirstPoll;
        while (true)
        {
            if (await TryTakeAsync(name, holder, cancellationToken) is { } leaseUntil)
            {
                return new Handle(this, name, holder, leaseUntil);
            }

            var remaining = timeout == Timeout.InfiniteTimeSpan ? poll : timeout - time.GetElapsedTime(started);
            if (remaining <= TimeSpan.Zero)
            {
                return null;
            }

            await Task.Delay(remaining < poll ? remaining : poll, time, cancellationToken);
            poll = poll * 2 < LongestPoll ? poll * 2 : LongestPoll;
        }
    }

    // The end of the new lease, or null while another holder has the lock. On a database with one writer, reading the row or
    // beginning the write can give up waiting for another connection's transaction: nothing was taken, and the lock counts
    // as held, since that transaction may be the holder's own work, which keeps it from renewing.
    private async Task<DateTimeOffset?> TryTakeAsync(string name, string holder, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        try
        {
            return await TakeAsync(context, name, holder, cancellationToken);
        }
        catch (Exception exception) when (scope.ServiceProvider.GetRequiredService<IDataProvider>().IsBusy(exception))
        {
            return null;
        }
    }

    private async Task<DateTimeOffset?> TakeAsync(SystemDbContext context, string name, string holder, CancellationToken cancellationToken)
    {
        var current = await context.Set<LockRow>().AsNoTracking().SingleOrDefaultAsync(row => row.Name == name, cancellationToken);
        if (current is { Owner: not null } && current.LeaseUntil > time.GetUtcNow())
        {
            return null;
        }

        // The writes are not canceled, so a take that commits always comes back as a handle that renews and releases it. On
        // SQLite the transaction begins once the database's one writer is free, so the lease starts from the clock then.
        await using var transaction = await context.Database.BeginTransactionAsync(CancellationToken.None);
        var now = time.GetUtcNow();
        var until = StorageConventions.ToMicroseconds(now + Lease);

        if (current is null)
        {
            context.Add(new LockRow { Name = name, Owner = holder, AcquiredAt = now, LeaseUntil = until });
            try
            {
                await context.SaveChangesAsync(CancellationToken.None);
                await transaction.CommitAsync(CancellationToken.None);
                return until;
            }
            catch (DbUpdateException)
            {
                // Another holder made the row first. Only the first use of a name inserts, so this is rare. The open
                // transaction holds the write lock, which a read on another connection of the in-memory database waits for.
                await transaction.RollbackAsync(CancellationToken.None);
                if (await ExistsAsync(name))
                {
                    return null;
                }

                throw;
            }
        }

        var taken = await context.Set<LockRow>()
            .Where(row => row.Name == name && (row.Owner == null || row.LeaseUntil == null || row.LeaseUntil <= now))
            .ExecuteUpdateAsync(
                row => row
                    .SetProperty(column => column.Owner, holder)
                    .SetProperty(column => column.AcquiredAt, now)
                    .SetProperty(column => column.LeaseUntil, until),
                CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);

        if (taken == 0)
        {
            return null;
        }

        if (current.Owner is not null)
        {
            LockLog.LeaseTakenOver(logger, name, current.Owner);
        }

        return until;
    }

    private async Task<bool> ExistsAsync(string name)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<LockRow>().AnyAsync(row => row.Name == name);
    }

    // The end of the renewed lease, or null when the holder no longer has the lock; Busy when the database has one writer,
    // the renewal gave up waiting for another connection's transaction, and the row may still name the holder.
    private async Task<(DateTimeOffset? Until, bool Busy)> RenewAsync(string name, string holder, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        var provider = scope.ServiceProvider.GetRequiredService<IDataProvider>();
        try
        {
            // As for a take, the lease starts from the clock once the transaction has begun.
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var until = StorageConventions.ToMicroseconds(time.GetUtcNow() + Lease);
            var renewed = await context.Set<LockRow>()
                .Where(row => row.Name == name && row.Owner == holder)
                .ExecuteUpdateAsync(row => row.SetProperty(column => column.LeaseUntil, until), cancellationToken);
            await transaction.CommitAsync(CancellationToken.None);

            return (renewed == 1 ? until : null, false);
        }
        catch (Exception exception) when (provider.IsBusy(exception))
        {
        }

        // A take-over can commit between two transactions that keep the renewal waiting. Reads do not wait for the writer
        // in WAL mode, so the row tells whether the lock is still the holder's; a read that waits as well tells nothing.
        try
        {
            var owner = await context.Set<LockRow>().AsNoTracking().Where(row => row.Name == name).Select(row => row.Owner).SingleOrDefaultAsync(cancellationToken);
            return (null, owner == holder);
        }
        catch (Exception exception) when (provider.IsBusy(exception))
        {
            return (null, true);
        }
    }

    private async Task ReleaseAsync(string name, string holder)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();

        await context.Set<LockRow>()
            .Where(row => row.Name == name && row.Owner == holder)
            .ExecuteUpdateAsync(row => row
                .SetProperty(column => column.Owner, (string?)null)
                .SetProperty(column => column.AcquiredAt, (DateTimeOffset?)null)
                .SetProperty(column => column.LeaseUntil, (DateTimeOffset?)null));
    }

    private ILogger Logger => logger;

    private DateTimeOffset Now() => time.GetUtcNow();

    private Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, time, cancellationToken);

    private sealed class Handle : IDistributedLockHandle
    {
        private readonly DatabaseDistributedLock _owner;
        private readonly string _holder;
        private readonly CancellationTokenSource _lost = new();
        private readonly CancellationTokenSource _stopping = new();
        private readonly Task _renewal;
        private DateTimeOffset _leaseUntil;
        private int _disposed;

        public Handle(DatabaseDistributedLock owner, string name, string holder, DateTimeOffset leaseUntil)
        {
            _owner = owner;
            _holder = holder;
            _leaseUntil = leaseUntil;
            Name = name;
            Lost = _lost.Token;
            _renewal = Task.Run(RenewAsync, CancellationToken.None);
        }

        public string Name { get; }

        public CancellationToken Lost { get; }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            await _stopping.CancelAsync();
            await _renewal;

            // Conditional on the holder, so it changes nothing if another took the lock.
            try
            {
                await _owner.ReleaseAsync(Name, _holder);
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception exception)
            {
                LockLog.ReleaseFailed(_owner.Logger, exception, Name);
            }

            _stopping.Dispose();
            _lost.Dispose();
        }

        // Renews the lease three times in each lease, so that a renewal that fails has two more chances before it runs out.
        private async Task RenewAsync()
        {
            var interval = _owner.Lease / 3;
            var delay = interval;
            var waiting = false;
            while (true)
            {
                try
                {
                    await _owner.DelayAsync(delay, _stopping.Token);
                    var (until, busy) = await _owner.RenewAsync(Name, _holder, _stopping.Token);

                    // While another connection holds the one writer of the database, such as the holder's own work in a
                    // long transaction, no other holder can take the lock either, since taking it is a write. So the lease
                    // does not count as run out, and the renewal waits again at once, to be waiting when that write ends.
                    if (busy)
                    {
                        if (!waiting)
                        {
                            LockLog.RenewWaiting(_owner.Logger, Name);
                            waiting = true;
                        }

                        delay = TimeSpan.Zero;
                        continue;
                    }

                    waiting = false;
                    delay = interval;
                    if (until is null)
                    {
                        await LoseAsync("another holder took it");
                        return;
                    }

                    _leaseUntil = until.Value;
                }
                catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    // The host's services are gone, so nothing renews or releases the lock any more.
                    await LoseAsync("the host stopped");
                    return;
                }
                catch (Exception exception)
                {
                    LockLog.RenewFailed(_owner.Logger, exception, Name);
                    if (_owner.Now() >= _leaseUntil)
                    {
                        await LoseAsync("its lease ran out");
                        return;
                    }
                }
            }
        }

        private async Task LoseAsync(string reason)
        {
            LockLog.Lost(_owner.Logger, Name, reason);
            await _lost.CancelAsync();
        }
    }
}
