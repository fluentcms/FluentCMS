using System.Data.Common;
using System.Diagnostics;
using FluentCMS.Core.Caching;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.IntegrationTests.Diagnostics;
using FluentCMS.Core.IntegrationTests.Testing;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Providers;
using FluentCMS.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Caching;

public sealed class DistributedLockTests : IAsyncDisposable
{
    private const string Name = "tests:lock";
    private const string Crashed = "crashed-host/1/0123456789abcdef0123456789abcdef";

    // Work whose commit the database refuses: a deferred foreign key that nothing satisfies by then.
    private const string Unsatisfied = """
        CREATE TABLE tests_parent (id INTEGER PRIMARY KEY);
        CREATE TABLE tests_child (parent INTEGER REFERENCES tests_parent (id) DEFERRABLE INITIALLY DEFERRED);
        INSERT INTO tests_child (parent) VALUES (1);
        """;

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    // A lease whose renewals, every third of it on the real clock, never come while a test runs: only what the test does
    // renews it, and its time passes on the clock the test moves.
    private static readonly TimeSpan OutOfReach = TimeSpan.FromDays(1);

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("fluentcms-lock-");
    private readonly string _file;
    private readonly CommandSignals _commands = CommandSignals.OfLock(Name);
    private readonly FluentCmsTestHost _first;
    private readonly FluentCmsTestHost _second;

    public DistributedLockTests()
    {
        _file = Path.Combine(_directory.FullName, "cms.db");
        _first = Watched(new FluentCmsTestHost().UseSqlite(_file), _commands);
        _second = Watched(new FluentCmsTestHost().UseSqlite(_file), _commands);
    }

    public async ValueTask DisposeAsync()
    {
        await _first.DisposeAsync();
        await _second.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try
        {
            _directory.Delete(recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task TheDefaultLockIsKeptInTheDatabaseWithNoConfiguration()
    {
        await using var host = new FluentCmsTestHost();
        var locks = host.Services.GetRequiredService<IDistributedLock>();
        locks.ShouldBeOfType<DatabaseDistributedLock>();

        await using (var held = await locks.TryAcquireAsync(Name, TimeSpan.Zero, Token))
        {
            held.ShouldNotBeNull().Name.ShouldBe(Name);
            (await RowAsync(host)).Owner.ShouldNotBeNull();
        }

        (await RowAsync(host)).Owner.ShouldBeNull();
    }

    [Fact]
    public async Task OnlyOneOfTwoConcurrentHoldersGetsTheLockAndTheOtherGetsItAfterRelease()
    {
        var (first, second) = BothInstances();

        var attempts = await Task.WhenAll(
            Task.Run(() => first.TryAcquireAsync(Name, TimeSpan.Zero, Token), Token),
            Task.Run(() => second.TryAcquireAsync(Name, TimeSpan.Zero, Token), Token));

        attempts.Count(held => held is not null).ShouldBe(1);
        var winner = attempts.Single(held => held is not null)!;
        var other = attempts[0] is null ? first : second;

        _commands.Reads.Forget();
        var waiting = other.TryAcquireAsync(Name, Patience, Token);

        // Two reads of the row: the waiter found the lock held, waited, and looked again.
        await _commands.Reads.NextAsync(2, Patience);
        waiting.IsCompleted.ShouldBeFalse();

        await winner.DisposeAsync();

        await using var next = await waiting;
        next.ShouldNotBeNull();
    }

    [Fact]
    public async Task HoldersOnTwoInstancesNeverHoldTheLockAtTheSameTime()
    {
        var (first, second) = BothInstances();
        var inside = 0;
        var most = 0;

        async Task WorkAsync(IDistributedLock locks)
        {
            for (var round = 0; round < 3; round++)
            {
                await using var held = await locks.TryAcquireAsync(Name, Patience, Token);
                held.ShouldNotBeNull();

                var now = Interlocked.Increment(ref inside);
                InterlockedMax(ref most, now);
                (await RowAsync(_first)).Owner.ShouldNotBeNull();
                Interlocked.Decrement(ref inside);
            }
        }

        await Task.WhenAll(Enumerable.Range(0, 4).Select(worker => Task.Run(() => WorkAsync(worker % 2 == 0 ? first : second), Token)));

        most.ShouldBe(1);
    }

    [Fact]
    public async Task AWaitThatTimesOutGetsNothing()
    {
        var (first, second) = BothInstances();
        await using var held = await first.TryAcquireAsync(Name, TimeSpan.Zero, Token);
        held.ShouldNotBeNull();

        (await second.TryAcquireAsync(Name, TimeSpan.FromMilliseconds(200), Token)).ShouldBeNull();
    }

    [Fact]
    public async Task AnExpiredLeaseCanBeTaken()
    {
        await using var host = new FluentCmsTestHost();
        var locks = host.Services.GetRequiredService<IDistributedLock>();

        // What a holder that stopped without releasing the lock leaves behind.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
            var now = host.Time.GetUtcNow();
            context.Add(new LockRow { Name = Name, Owner = Crashed, AcquiredAt = now, LeaseUntil = now + TimeSpan.FromSeconds(30) });
            await context.SaveChangesAsync(Token);
        }

        (await locks.TryAcquireAsync(Name, TimeSpan.Zero, Token)).ShouldBeNull();

        host.Time.Advance(TimeSpan.FromSeconds(31));

        await using var taken = await locks.TryAcquireAsync(Name, TimeSpan.Zero, Token);
        taken.ShouldNotBeNull();
        var row = await RowAsync(host);
        row.Owner.ShouldNotBe(Crashed);
        row.LeaseUntil.ShouldBe(host.Time.GetUtcNow() + TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task AHolderRenewsItsLease()
    {
        var commands = CommandSignals.OfLock(Name);
        await using var host = Watched(new FluentCmsTestHost(), commands);
        var locks = ShortLease(host);
        await using var held = await locks.TryAcquireAsync(Name, TimeSpan.Zero, Token);
        held.ShouldNotBeNull();
        var first = (await RowAsync(host)).LeaseUntil!.Value;

        // A renewal under way may have read the clock before it moved; the one after it has not.
        host.Time.Advance(TimeSpan.FromSeconds(1));
        commands.Writes.Forget();
        await commands.Writes.NextAsync(2, Patience);

        (await RowAsync(host)).LeaseUntil!.Value.ShouldBeGreaterThan(first);
        (await locks.TryAcquireAsync(Name, TimeSpan.Zero, Token)).ShouldBeNull();
        held.Lost.IsCancellationRequested.ShouldBeFalse();
    }

    [Fact]
    public async Task AHolderIsToldWhenAnotherTookTheLockAndItsReleaseLeavesTheOtherAlone()
    {
        await using var host = new FluentCmsTestHost();
        var locks = ShortLease(host);
        var held = await locks.TryAcquireAsync(Name, TimeSpan.Zero, Token);
        held.ShouldNotBeNull();

        // What another instance does once the lease ran out while this holder was paused.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<LockRow>()
                .Where(row => row.Name == Name)
                .ExecuteUpdateAsync(row => row.SetProperty(column => column.Owner, Crashed), Token);
        }

        var lost = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (held.Lost.Register(() => lost.TrySetResult()))
        {
            await lost.Task.WaitAsync(Patience, Token);
        }

        held.Lost.IsCancellationRequested.ShouldBeTrue();

        await held.DisposeAsync();
        (await RowAsync(host)).Owner.ShouldBe(Crashed);
    }

    // A callback of the lock's caller on Lost that throws is logged with what it threw, once and under an event of its own.
    // The other callbacks run and the lock is lost all the same. Before, the renewal loop took it for a renewal that failed
    // and told the loss a second time.
    [Fact]
    public async Task ACallbackOnLostThatThrowsIsLoggedAndTheLockIsLostAllTheSame()
    {
        await using var host = new FluentCmsTestHost();
        var logs = new LogCapture();
        var held = await Lock(host, host.Time, TimeSpan.FromMilliseconds(300), logs).TryAcquireAsync(Name, TimeSpan.Zero, Token);
        held.ShouldNotBeNull();
        var lost = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var told = held.Lost.Register(() => lost.TrySetResult());
        using var throws = held.Lost.Register(() => throw new InvalidOperationException("A callback of the lock's caller."));

        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<LockRow>()
                .Where(row => row.Name == Name)
                .ExecuteUpdateAsync(row => row.SetProperty(column => column.Owner, Crashed), Token);
        }

        await lost.Task.WaitAsync(Patience, Token);
        await held.DisposeAsync();

        // A renewal that waited behind the host's work at startup says so, which is no part of this.
        var entries = logs.Entries.Where(entry => entry.EventId.Id != LockLog.RenewWaitingId).ToList();
        entries.Select(entry => entry.EventId.Id).ShouldBe([LockLog.LostId, LockLog.LostCallbackFailedId]);
        entries[1].Exception.ShouldBeOfType<AggregateException>().InnerExceptions.ShouldHaveSingleItem().ShouldBeOfType<InvalidOperationException>();
    }

    // SQLite has one writer, so a renewal waits for another connection's write transaction, such as the holder's own work,
    // and gives up before it ends: after about 5 seconds here, where a host waits about 35. It then reads the row.
    [Fact]
    public async Task AHolderKeepsTheLockWhileAWriteTransactionOutlastsItsLease()
    {
        var commands = CommandSignals.OfLock(Name);
        await using var host = Impatient(Watched(new FluentCmsTestHost().UseSqlite(_file), commands));
        await using var held = await RealClockShortLease(host).TryAcquireAsync(Name, TimeSpan.Zero, Token);
        held.ShouldNotBeNull();

        await using (var writer = await WriteTransactionAsync())
        {
            commands.Reads.Forget();
            await commands.Reads.NextAsync(1, Patience);
            held.Lost.IsCancellationRequested.ShouldBeFalse();
        }

        // The renewal after the transaction finds the lock still the holder's.
        commands.Writes.Forget();
        await commands.Writes.NextAsync(1, Patience);
        held.Lost.IsCancellationRequested.ShouldBeFalse();
    }

    [Fact]
    public async Task AWaiterOnADatabaseThatAWriteTransactionHoldsEndsWithItsTimeout()
    {
        await using var host = Impatient(new FluentCmsTestHost().UseSqlite(_file));
        var waiter = RealClockShortLease(host);
        var held = await RealClockShortLease(_first).TryAcquireAsync(Name, TimeSpan.Zero, Token);
        held.ShouldNotBeNull();

        // The holder cannot renew while the transaction lasts, so the waiter finds the lease over and tries to take it.
        await using (var writer = await WriteTransactionAsync())
        {
            (await waiter.TryAcquireAsync(Name, TimeSpan.FromMilliseconds(500), Token)).ShouldBeNull();
        }

        await held.DisposeAsync();
        await using var next = await waiter.TryAcquireAsync(Name, Patience, Token);
        next.ShouldNotBeNull();
    }

    // A renewal that waited for the writer longer than the lease of 3 seconds would have written one that was already over.
    [Fact]
    public async Task ARenewalThatWaitedForTheWriterStartsItsLeaseWhenItIsWritten()
    {
        await using var host = new FluentCmsTestHost().UseSqlite(_file);
        await using var held = await RealClockLease(host, TimeSpan.FromSeconds(3)).TryAcquireAsync(Name, TimeSpan.Zero, Token);
        held.ShouldNotBeNull();
        var taken = (await RowAsync(host)).LeaseUntil;

        DateTimeOffset released;
        await using (var writer = await WriteTransactionAsync())
        {
            await Task.Delay(TimeSpan.FromSeconds(5), Token);
            released = DateTimeOffset.UtcNow;
        }

        // The lease the waited renewal committed, which the next renewal a second later would replace.
        var renewed = await NextLeaseAsync(host, taken);
        renewed.ShouldBeGreaterThan(released);
        held.Lost.IsCancellationRequested.ShouldBeFalse();
    }

    [Fact]
    public async Task ATakeThatWaitedForTheWriterStartsItsLeaseWhenItIsWritten()
    {
        var locks = RealClockLease(_first, TimeSpan.FromSeconds(3));
        await (await locks.TryAcquireAsync(Name, TimeSpan.Zero, Token)).ShouldNotBeNull().DisposeAsync();

        Task<IDistributedLockHandle?> waiting;
        await using (var writer = await WriteTransactionAsync())
        {
            // Microsoft.Data.Sqlite runs its asynchronous calls synchronously, so the take that waits needs a thread.
            waiting = Task.Run(() => locks.TryAcquireAsync(Name, Patience, Token), Token);
            await Task.Delay(TimeSpan.FromSeconds(4), Token);
        }

        await using var held = await waiting;
        held.ShouldNotBeNull();
        (await RowAsync(_first)).LeaseUntil.ShouldNotBeNull().ShouldBeGreaterThan(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task AFirstUseOnADatabaseThatAWriteTransactionHoldsEndsWithItsTimeout()
    {
        await using var host = Impatient(new FluentCmsTestHost().UseSqlite(_file));
        var locks = RealClockShortLease(host);

        await using (var writer = await WriteTransactionAsync())
        {
            (await locks.TryAcquireAsync(Name, TimeSpan.FromMilliseconds(500), Token)).ShouldBeNull();
        }

        await using var held = await locks.TryAcquireAsync(Name, Patience, Token);
        held.ShouldNotBeNull();
    }

    // What another instance does in the moment between two write transactions that keep the holder's renewals waiting.
    [Fact]
    public async Task AHolderIsToldWhenAnotherTookTheLockWhileAWriteTransactionKeepsItsRenewalsWaiting()
    {
        await using var host = Impatient(new FluentCmsTestHost().UseSqlite(_file));
        await using var held = await RealClockShortLease(host).TryAcquireAsync(Name, TimeSpan.Zero, Token);
        held.ShouldNotBeNull();

        await using var writer = new SqliteConnection($"Data Source={_file};Pooling=False");
        await writer.OpenAsync(Token);
        await using (var command = writer.CreateCommand())
        {
            command.CommandText = $"UPDATE cms_lock SET owner = '{Crashed}' WHERE name = '{Name}'; BEGIN IMMEDIATE";
            await command.ExecuteNonQueryAsync(Token);
        }

        var lost = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (held.Lost.Register(() => lost.TrySetResult()))
        {
            await lost.Task.WaitAsync(Patience, Token);
        }
    }

    // A waiter that found the lease over while a write transaction kept the holder's renewal waiting gets the writer first
    // when the transaction ends more often than not, since it began to wait last and SQLite sleeps less between its first
    // tries. It takes nothing over then, and the renewal, which has been trying all along, renews the lease before the
    // waiter's confirming try a second later: in a median of 80 milliseconds after the transaction in a review's 50 rounds,
    // and at most 660.
    [Fact]
    public async Task AHolderKeepsTheLockWhenAWaiterFindsItsLeaseOverAtTheEndOfAWriteTransaction()
    {
        var takes = new TakeSignals(Name);
        var logs = new LogCapture();
        await using var holderHost = Impatient(new FluentCmsTestHost().UseSqlite(_file));
        await using var waiterHost = Impatient(Watched(new FluentCmsTestHost().UseSqlite(_file), takes));
        var waiter = RealClockLease(waiterHost, TimeSpan.FromMilliseconds(300), logs);
        await using var held = await RealClockShortLease(holderHost).TryAcquireAsync(Name, TimeSpan.Zero, Token);
        held.ShouldNotBeNull();
        var holder = (await RowAsync(holderHost)).Owner;

        for (var round = 0; round < 10; round++)
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
            Task<IDistributedLockHandle?> waiting;
            await using (var writer = await WriteTransactionAsync())
            {
                takes.Begins.Forget();
                waiting = Task.Run(() => waiter.TryAcquireAsync(Name, Patience, stop.Token), Token);
                await takes.Begins.NextAsync(1, Patience);
            }

            (await LiveRowAsync(holderHost)).Owner.ShouldBe(holder, $"the waiter took the lock in round {round}");

            // The tries of the waiter's confirmation, which find the lease renewed.
            takes.Reads.Forget();
            await takes.Reads.NextAsync(3, Patience);
            (await RowAsync(holderHost)).Owner.ShouldBe(holder, $"the waiter took the lock in round {round}");
            await stop.CancelAsync();
            (await CanceledAsync(waiting)).ShouldBeNull();
        }

        held.Lost.IsCancellationRequested.ShouldBeFalse();
        logs.Entries.ShouldNotContain(entry => entry.EventId.Id == LockLog.LeaseTakenOverId);
    }

    // A holder that stopped leaves its lease over and as it was, so a waiter takes it over at the try a second after it saw
    // it so: two tries that each began a write transaction.
    [Fact]
    public async Task AWaiterTakesOverTheLeaseOfAHolderThatStoppedOnceItsConfirmationFindsItAsItWas()
    {
        var takes = new TakeSignals(Name);
        var logs = new LogCapture();
        await using var host = Watched(new FluentCmsTestHost().UseSqlite(_file), takes);
        var locks = RealClockLease(host, TimeSpan.FromSeconds(30), logs);
        await InsertAsync(host, Crashed, DateTimeOffset.UtcNow - TimeSpan.FromSeconds(1));

        var watch = Stopwatch.StartNew();
        await using var taken = await locks.TryAcquireAsync(Name, Patience, Token);

        taken.ShouldNotBeNull();
        takes.Count.ShouldBeGreaterThanOrEqualTo(2);
        watch.Elapsed.ShouldBeGreaterThan(TimeSpan.FromMilliseconds(900));
        var takeOver = logs.Entries.ShouldHaveSingleItem();
        takeOver.EventId.Id.ShouldBe(LockLog.LeaseTakenOverId);
        takeOver.State["Holder"].ShouldBe(Crashed);
    }

    // The export and the import try once, and take over the lock of a predecessor that stopped, a confirmation late.
    [Fact]
    public async Task ATryOnceTakesOverTheLeaseOfAHolderThatStoppedEveryTime()
    {
        var locks = RealClockLease(_first, TimeSpan.FromSeconds(30));
        await InsertAsync(_first, Crashed, DateTimeOffset.UtcNow - TimeSpan.FromSeconds(1));

        for (var round = 0; round < 30; round++)
        {
            var watch = Stopwatch.StartNew();
            var taken = await locks.TryAcquireAsync(Name, TimeSpan.Zero, Token);
            taken.ShouldNotBeNull($"round {round}, after {watch.Elapsed}");
            await taken.DisposeAsync();

            await using var scope = _first.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<LockRow>()
                .Where(row => row.Name == Name)
                .ExecuteUpdateAsync(row => row.SetProperty(column => column.Owner, Crashed).SetProperty(column => column.LeaseUntil, DateTimeOffset.UtcNow - TimeSpan.FromSeconds(1)), Token);
        }
    }

    [Fact]
    public async Task OfTwoWaitersOnTheLeaseOfAHolderThatStoppedOneTakesIt()
    {
        await InsertAsync(_first, Crashed, DateTimeOffset.UtcNow - TimeSpan.FromSeconds(1));
        var first = RealClockLease(_first, TimeSpan.FromSeconds(30));
        var second = RealClockLease(_second, TimeSpan.FromSeconds(30));
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);

        var waits = new[]
        {
            Task.Run(() => first.TryAcquireAsync(Name, Patience, stop.Token), Token),
            Task.Run(() => second.TryAcquireAsync(Name, Patience, stop.Token), Token),
        };

        // The loser waits for the winner, which keeps the lock, so it is canceled once one has it.
        var winner = await (await Task.WhenAny(waits));
        await stop.CancelAsync();
        var attempts = new[] { await CanceledAsync(waits[0]), await CanceledAsync(waits[1]) };

        winner.ShouldNotBeNull();
        attempts.Count(held => held is not null).ShouldBe(1);
        await winner.DisposeAsync();
    }

    // The holder releases the lock while a waiter that found its lease over waits for the writer, so the waiter takes a free
    // lock, which no holder left behind.
    [Fact]
    public async Task AWaiterThatGetsALockReleasedWhileItWaitedForTheWriterLogsNoTakeOver()
    {
        var takes = new TakeSignals(Name);
        var logs = new LogCapture();
        await using var holderHost = Impatient(new FluentCmsTestHost().UseSqlite(_file));
        await using var waiterHost = Impatient(Watched(new FluentCmsTestHost().UseSqlite(_file), takes));
        var waiter = RealClockLease(waiterHost, TimeSpan.FromMilliseconds(300), logs);
        var held = await RealClockShortLease(holderHost).TryAcquireAsync(Name, TimeSpan.Zero, Token);
        held.ShouldNotBeNull();

        Task<IDistributedLockHandle?> waiting;
        Task releasing;
        await using (var writer = await WriteTransactionAsync())
        {
            waiting = Task.Run(() => waiter.TryAcquireAsync(Name, Patience, Token), Token);
            await takes.Begins.NextAsync(1, Patience);
            releasing = Task.Run(() => held.DisposeAsync().AsTask(), Token);
        }

        await releasing.WaitAsync(Patience, Token);
        await using var next = await waiting;
        next.ShouldNotBeNull();
        logs.Entries.ShouldNotContain(entry => entry.EventId.Id == LockLog.LeaseTakenOverId);
    }

    // The holder's own work keeps the writer past the lease. When it commits, the waiter, which found the lease over, gets the
    // writer, and the holder's renewal, a third of the lease of 30 seconds away on the real clock, does not try for it. The
    // commit renewed the lease, so the waiter finds it held and keeps polling; before, its try a second on took it over.
    [Fact]
    public async Task AHolderKeepsTheLockWhenAWaiterGetsTheWriterFirstAtTheEndOfTheHoldersOwnWriteTransaction()
    {
        var clock = new ControllableTimeProvider();
        var takes = new TakeSignals(Name);
        var logs = new LogCapture();
        await using var waiterHost = Watched(new FluentCmsTestHost().UseSqlite(_file), takes);
        var waiter = Lock(waiterHost, clock, TimeSpan.FromSeconds(30), logs);
        await using var held = await Lock(_first, clock, TimeSpan.FromSeconds(30)).TryAcquireAsync(Name, TimeSpan.Zero, Token);
        held.ShouldNotBeNull();
        var holder = (await RowAsync(_first)).Owner;

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
        Task<IDistributedLockHandle?> waiting;
        await using (var scope = _first.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
            await using var work = await context.Database.BeginTransactionAsync(Token);
            context.Add(new LockRow { Name = "tests:work" });
            await context.SaveChangesAsync(Token);

            clock.Advance(TimeSpan.FromSeconds(31));
            waiting = Task.Run(() => waiter.TryAcquireAsync(Name, Patience, stop.Token), Token);
            await takes.Begins.NextAsync(1, Patience);
            takes.Reads.Forget();
            await work.CommitAsync(Token);
        }

        // The read inside the waiter's transaction and its polls 50, 150, 350, 750 and 1,550 milliseconds on.
        await Task.WhenAny(waiting, takes.Reads.NextAsync(6, Patience));
        waiting.IsCompleted.ShouldBeFalse("the waiter took the lock");
        (await RowAsync(_first)).Owner.ShouldBe(holder);
        await stop.CancelAsync();
        (await CanceledAsync(waiting)).ShouldBeNull();
        held.Lost.IsCancellationRequested.ShouldBeFalse();
        logs.Entries.ShouldNotContain(entry => entry.EventId.Id == LockLog.LeaseTakenOverId);
    }

    // A holder that stopped renewing, and whose work commits no write transaction that would renew it, is taken over once its
    // lease has run out and the try a second later finds it as it was.
    [Fact]
    public async Task AWaiterTakesOverTheLockOfAHolderThatStoppedRenewingWhileItsWorkCommitsNothing()
    {
        var clock = new ControllableTimeProvider();
        var logs = new LogCapture();
        var held = await Lock(_first, clock, OutOfReach, renewals: new LeaseRenewalInterceptor()).TryAcquireAsync(Name, TimeSpan.Zero, Token);
        held.ShouldNotBeNull();
        var holder = (await RowAsync(_first)).Owner;

        // A try whose BEGIN waited for the hosts' work at startup adds a second, so the wait has the suite's patience.
        clock.Advance(OutOfReach + TimeSpan.FromSeconds(1));
        await using var taken = await Lock(_second, clock, TimeSpan.FromSeconds(30), logs).TryAcquireAsync(Name, Patience, Token);

        taken.ShouldNotBeNull();
        var takeOver = logs.Entries.ShouldHaveSingleItem();
        takeOver.EventId.Id.ShouldBe(LockLog.LeaseTakenOverId);
        takeOver.State["Holder"].ShouldBe(holder);
        await held.DisposeAsync();
        (await RowAsync(_first)).Owner.ShouldNotBe(holder);
    }

    // The renewals every third of the lease leave half of it to pass only while they wait, so a commit renews it from then on,
    // asynchronous or not. A commit before writes nothing more, and neither does one after a commit that renewed, since the
    // handle then knows the lease it has.
    [Fact]
    public async Task AWriteTransactionOfTheHoldersWorkRenewsTheLeaseAsItCommitsOnceHalfOfItHasPassed()
    {
        var clock = new ControllableTimeProvider();
        var renewals = new LeaseRenewalInterceptor();
        await using var held = await Lock(_first, clock, OutOfReach, renewals: renewals).TryAcquireAsync(Name, TimeSpan.Zero, Token);
        held.ShouldNotBeNull();
        var taken = (await RowAsync(_first)).LeaseUntil;

        clock.Advance(OutOfReach / 2 - TimeSpan.FromSeconds(1));
        await CommitAsync(_first, renewals);
        (await RowAsync(_first)).LeaseUntil.ShouldBe(taken);

        clock.Advance(TimeSpan.FromSeconds(2));
        await CommitAsync(_first, renewals);
        var renewed = clock.GetUtcNow() + OutOfReach;
        (await RowAsync(_first)).LeaseUntil.ShouldBe(renewed);

        clock.Advance(TimeSpan.FromSeconds(1));
        await CommitAsync(_first, renewals);
        (await RowAsync(_first)).LeaseUntil.ShouldBe(renewed);

        clock.Advance(OutOfReach / 2 + TimeSpan.FromSeconds(1));
        Commit(_first, renewals);
        renewed = clock.GetUtcNow() + OutOfReach;
        (await RowAsync(_first)).LeaseUntil.ShouldBe(renewed);

        clock.Advance(TimeSpan.FromSeconds(1));
        Commit(_first, renewals);
        (await RowAsync(_first)).LeaseUntil.ShouldBe(renewed);
        held.Lost.IsCancellationRequested.ShouldBeFalse();
    }

    // A commit that fails rolls the renewal inside it back, so the row keeps its lease and the handle does not take it for
    // renewed: the next commit renews it. Before, the handle did, and nothing renewed the row for half a lease.
    [Fact]
    public async Task ACommitThatFailsLeavesTheLeaseForTheNextCommitToRenew()
    {
        var clock = new ControllableTimeProvider();
        var renewals = new LeaseRenewalInterceptor();
        await using var held = await Lock(_first, clock, OutOfReach, renewals: renewals).TryAcquireAsync(Name, TimeSpan.Zero, Token);
        held.ShouldNotBeNull();
        var taken = (await RowAsync(_first)).LeaseUntil;

        clock.Advance(OutOfReach / 2 + TimeSpan.FromSeconds(1));
        await FailToCommitAsync(_first, renewals);
        FailToCommit(_first, renewals);
        (await RowAsync(_first)).LeaseUntil.ShouldBe(taken);

        await CommitAsync(_first, renewals);
        (await RowAsync(_first)).LeaseUntil.ShouldBe(clock.GetUtcNow() + OutOfReach);
        held.Lost.IsCancellationRequested.ShouldBeFalse();
    }

    [Fact]
    public async Task ARenewalThatFailsInsideACommitIsLoggedAndTheCommitGoesOn()
    {
        var clock = new ControllableTimeProvider();
        var logs = new LogCapture();
        var renewals = new LeaseRenewalInterceptor();
        var failing = new FailingRenewals(Name);
        await using var host = Watched(new FluentCmsTestHost().UseSqlite(_file), failing);
        await using var held = await Lock(host, clock, OutOfReach, logs, renewals).TryAcquireAsync(Name, TimeSpan.Zero, Token);
        held.ShouldNotBeNull();
        var taken = (await RowAsync(host)).LeaseUntil;

        clock.Advance(OutOfReach / 2 + TimeSpan.FromSeconds(1));
        failing.On = true;
        await CommitAsync(host, renewals);
        Commit(host, renewals);
        failing.On = false;

        logs.Entries.Select(entry => entry.EventId.Id).ShouldBe([LockLog.RenewFailedId, LockLog.RenewFailedId]);
        (await WorkAsync(host)).ShouldBe(2);
        (await RowAsync(host)).LeaseUntil.ShouldBe(taken);
        held.Lost.IsCancellationRequested.ShouldBeFalse();

        // The handle did not take the lease for renewed either.
        await CommitAsync(host, renewals);
        (await RowAsync(host)).LeaseUntil.ShouldBe(clock.GetUtcNow() + OutOfReach);
    }

    // Another holder took the lock between two transactions of the holder's work. The renewal inside the next commit finds
    // the row another's, and the holder is told when that commit is through, where the loop's next renewal is a third of the
    // lease away. The callbacks on Lost run apart from the commit: one that waits for it sees it through, and one that
    // throws fails nothing and is logged.
    [Fact]
    public async Task AHolderIsToldWhenARenewalInsideACommitFindsThatAnotherTookTheLock()
    {
        var clock = new ControllableTimeProvider();
        var logs = new LogCapture();
        var renewals = new LeaseRenewalInterceptor();
        var held = await Lock(_first, clock, OutOfReach, logs, renewals).TryAcquireAsync(Name, TimeSpan.Zero, Token);
        held.ShouldNotBeNull();
        await using (var scope = _first.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<LockRow>()
                .Where(row => row.Name == Name)
                .ExecuteUpdateAsync(row => row.SetProperty(column => column.Owner, Crashed), Token);
        }

        // Registered last, the callback that waits runs first.
        using var committed = new ManualResetEventSlim();
        var sawTheCommit = false;
        var lost = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var told = held.Lost.Register(() => lost.TrySetResult());
        using var waits = held.Lost.Register(() =>
        {
            sawTheCommit = committed.Wait(Patience);
            throw new InvalidOperationException("A callback of the lock's caller.");
        });

        clock.Advance(OutOfReach / 2 + TimeSpan.FromSeconds(1));
        await CommitAsync(_first, renewals);
        committed.Set();
        await lost.Task.WaitAsync(Patience, Token);

        sawTheCommit.ShouldBeTrue();
        (await WorkAsync(_first)).ShouldBe(1);
        await held.DisposeAsync();
        logs.Entries.Select(entry => entry.EventId.Id).ShouldBe([LockLog.LostId, LockLog.LostCallbackFailedId]);
        (await RowAsync(_first)).Owner.ShouldBe(Crashed);
    }

    // A callback on Lost that does not return keeps the disposal of the handle waiting only as long as the job runner's
    // disposal waits for its loops, which is logged (#3544). The source of Lost is not disposed under the callback, which can
    // still use the token, and what it throws once it returns is logged as before. Before, the disposal waited for it.
    [Fact]
    public async Task ACallbackOnLostThatDoesNotReturnKeepsTheDisposalWaitingOnlyForItsBound()
    {
        var clock = new ControllableTimeProvider();
        var bound = new HeldTimers(clock, DisposeWait.Bound);
        var logs = new LogCapture();
        var failed = new LoggedSignal(LockLog.LostCallbackFailedId);
        var renewals = new LeaseRenewalInterceptor();
        var locks = new DatabaseDistributedLock(
            _first.Services.GetRequiredService<IServiceScopeFactory>(),
            bound,
            new Logger<DatabaseDistributedLock>(new LoggerFactory([logs, failed])),
            renewals)
        {
            Lease = OutOfReach,
        };
        var held = await locks.TryAcquireAsync(Name, TimeSpan.Zero, Token);
        held.ShouldNotBeNull();
        await using (var scope = _first.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<LockRow>()
                .Where(row => row.Name == Name)
                .ExecuteUpdateAsync(row => row.SetProperty(column => column.Owner, Crashed), Token);
        }

        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var returning = new ManualResetEventSlim();
        Exception? usingTheToken = null;
        using var waits = held.Lost.Register(() =>
        {
            running.TrySetResult();
            returning.Wait(Patience);
            try
            {
                held.Lost.WaitHandle.WaitOne(0);
            }
            catch (ObjectDisposedException exception)
            {
                usingTheToken = exception;
            }

            throw new InvalidOperationException("A callback of the lock's caller.");
        });

        clock.Advance(OutOfReach / 2 + TimeSpan.FromSeconds(1));
        await CommitAsync(_first, renewals);
        await running.Task.WaitAsync(Patience, Token);

        var disposing = held.DisposeAsync().AsTask();
        var expire = await bound.Held.WaitAsync(Patience, Token);
        disposing.IsCompleted.ShouldBeFalse();
        expire();
        await disposing.WaitAsync(Patience, Token);

        logs.Entries.Select(entry => entry.EventId.Id).ShouldBe([LockLog.LostId, LockLog.LostCallbackSlowId]);
        (await RowAsync(_first)).Owner.ShouldBe(Crashed);

        returning.Set();
        await failed.Logged.WaitAsync(Patience, Token);
        usingTheToken.ShouldBeNull();
        logs.Entries.Select(entry => entry.EventId.Id).ShouldBe([LockLog.LostId, LockLog.LostCallbackSlowId, LockLog.LostCallbackFailedId]);
        logs.Entries[2].Exception.ShouldBeOfType<AggregateException>().InnerExceptions.ShouldHaveSingleItem().ShouldBeOfType<InvalidOperationException>();
    }

    // What a commit does that read the handle as held just before it was disposed: its renewal finds the row released,
    // which is no loss, and the source of Lost is disposed.
    [Fact]
    public async Task ARenewalInsideACommitThatFindsTheLockReleasedByItsOwnHandleTellsNoLoss()
    {
        var clock = new ControllableTimeProvider();
        var logs = new LogCapture();
        var renewals = new LeaseRenewalInterceptor();
        var held = await Lock(_first, clock, OutOfReach, logs, renewals).TryAcquireAsync(Name, TimeSpan.Zero, Token);
        held.ShouldNotBeNull();
        await held.DisposeAsync();

        renewals.Add((DatabaseDistributedLock.Handle)held);
        clock.Advance(OutOfReach / 2 + TimeSpan.FromSeconds(1));
        await CommitAsync(_first, renewals);

        (await WorkAsync(_first)).ShouldBe(1);
        held.Lost.IsCancellationRequested.ShouldBeFalse();
        logs.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheContentAndSystemContextsOfAHostOnSqliteRenewItsLocksAsTheyCommit()
    {
        await using var scope = _first.Services.CreateAsyncScope();
        var renewals = _first.Services.GetRequiredService<LeaseRenewalInterceptor>();

        DbContext[] contexts = [scope.ServiceProvider.GetRequiredService<CmsDbContext>(), scope.ServiceProvider.GetRequiredService<SystemDbContext>()];
        foreach (var context in contexts)
        {
            context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()!.Interceptors!.ShouldContain(renewals);
        }
    }

    // The row is free, and another connection keeps the database's one writer for longer than the take waits.
    [Fact]
    public async Task AWaitThatTimesOutOnABusyDatabaseSaysSo()
    {
        await using var host = Impatient(new FluentCmsTestHost().UseSqlite(_file));
        var logs = new LogCapture();
        var locks = RealClockLease(host, TimeSpan.FromMilliseconds(300), logs);
        await (await locks.TryAcquireAsync(Name, TimeSpan.Zero, Token)).ShouldNotBeNull().DisposeAsync();

        await using (var writer = await WriteTransactionAsync())
        {
            (await locks.TryAcquireAsync(Name, TimeSpan.Zero, Token)).ShouldBeNull();
        }

        logs.Entries.ShouldHaveSingleItem().EventId.Id.ShouldBe(LockLog.TimedOutBusyId);
    }

    [Fact]
    public async Task ABadNameOrTimeoutIsRefused()
    {
        await using var host = new FluentCmsTestHost();
        var locks = host.Services.GetRequiredService<IDistributedLock>();

        await Should.ThrowAsync<ArgumentException>(() => locks.TryAcquireAsync(" ", TimeSpan.Zero, Token));
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => locks.TryAcquireAsync(new string('a', LockRow.NameMaxLength + 1), TimeSpan.Zero, Token));
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => locks.TryAcquireAsync(Name, TimeSpan.FromSeconds(-2), Token));
    }

    // The first use of a name inserts its row, so takes that start together race on the insert, and a loser reads the row
    // on another connection. On the in-memory database that read waited for the loser's own transaction, for 30 seconds.
    [Fact]
    public async Task TakesThatRaceOnTheFirstUseOfANameEndWithinTheirTimeout()
    {
        await using var host = new FluentCmsTestHost();
        var locks = host.Services.GetRequiredService<IDistributedLock>();
        var watch = Stopwatch.StartNew();

        var attempts = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() => locks.TryAcquireAsync(Name, TimeSpan.FromMilliseconds(500), Token), Token)));

        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
        attempts.Count(held => held is not null).ShouldBe(1);
        foreach (var held in attempts.OfType<IDistributedLockHandle>())
        {
            await held.DisposeAsync();
        }
    }

    // The second instance starts after the first has migrated the database, as the instances of a deployment do.
    private (IDistributedLock First, IDistributedLock Second) BothInstances()
    {
        var first = _first.Services.GetRequiredService<IDistributedLock>();
        return (first, _second.Services.GetRequiredService<IDistributedLock>());
    }

    // A lease of 300 milliseconds, renewed every 100, so that a test sees the renewals.
    private static DatabaseDistributedLock ShortLease(FluentCmsTestHost host) => Lock(host, host.Time, TimeSpan.FromMilliseconds(300));

    // A lease of 300 milliseconds on the real clock, renewed every 100, for a test whose time passes in SQLite's waits.
    private static DatabaseDistributedLock RealClockShortLease(FluentCmsTestHost host) => RealClockLease(host, TimeSpan.FromMilliseconds(300));

    private static DatabaseDistributedLock RealClockLease(FluentCmsTestHost host, TimeSpan lease, LogCapture? logs = null) => Lock(host, TimeProvider.System, lease, logs);

    // A lock of the host, whose write transactions renew what it holds as they commit; or, with renewals of its own, a lock
    // that only the commits of the contexts a test gives them renew, apart from the host's work in the background.
    private static DatabaseDistributedLock Lock(FluentCmsTestHost host, TimeProvider clock, TimeSpan lease, LogCapture? logs = null, LeaseRenewalInterceptor? renewals = null) => new(
        host.Services.GetRequiredService<IServiceScopeFactory>(),
        clock,
        logs is null ? NullLogger<DatabaseDistributedLock>.Instance : new Logger<DatabaseDistributedLock>(new LoggerFactory([logs])),
        renewals ?? host.Services.GetRequiredService<LeaseRenewalInterceptor>())
    {
        Lease = lease,
    };

    // A write transaction of the work that holds a lock, on a context of the host with the renewals given.
    private static async Task CommitAsync(FluentCmsTestHost host, LeaseRenewalInterceptor renewals)
    {
        await using var scope = host.Services.CreateAsyncScope();
        await using var context = Renewing(scope.ServiceProvider, renewals);
        await using var work = await context.Database.BeginTransactionAsync(Token);
        context.Add(new LockRow { Name = $"tests:work:{Guid.NewGuid():N}" });
        await context.SaveChangesAsync(Token);
        await work.CommitAsync(Token);
    }

    private static void Commit(FluentCmsTestHost host, LeaseRenewalInterceptor renewals)
    {
        using var scope = host.Services.CreateScope();
        using var context = Renewing(scope.ServiceProvider, renewals);
        using var work = context.Database.BeginTransaction();
        context.Add(new LockRow { Name = $"tests:work:{Guid.NewGuid():N}" });
        context.SaveChanges();
        work.Commit();
    }

    private static async Task FailToCommitAsync(FluentCmsTestHost host, LeaseRenewalInterceptor renewals)
    {
        await using var scope = host.Services.CreateAsyncScope();
        await using var context = Renewing(scope.ServiceProvider, renewals);
        await using var work = await context.Database.BeginTransactionAsync(Token);
        await context.Database.ExecuteSqlRawAsync(Unsatisfied, Token);
        (await Should.ThrowAsync<SqliteException>(() => work.CommitAsync(Token))).Message.ShouldContain("FOREIGN KEY");
    }

    private static void FailToCommit(FluentCmsTestHost host, LeaseRenewalInterceptor renewals)
    {
        using var scope = host.Services.CreateScope();
        using var context = Renewing(scope.ServiceProvider, renewals);
        using var work = context.Database.BeginTransaction();
        context.Database.ExecuteSqlRaw(Unsatisfied);
        Should.Throw<SqliteException>(work.Commit).Message.ShouldContain("FOREIGN KEY");
    }

    // The transactions of the work that committed.
    private static async Task<int> WorkAsync(FluentCmsTestHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<LockRow>().CountAsync(row => row.Name.StartsWith("tests:work:"), Token);
    }

    private static SystemDbContext Renewing(IServiceProvider services, LeaseRenewalInterceptor renewals) =>
        new(new DbContextOptionsBuilder<SystemDbContext>(services.GetRequiredService<DbContextOptions<SystemDbContext>>()).AddInterceptors(renewals).Options);

    // What a holder that stopped without releasing the lock leaves behind.
    private static async Task InsertAsync(FluentCmsTestHost host, string owner, DateTimeOffset leaseUntil)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        context.Add(new LockRow { Name = Name, Owner = owner, AcquiredAt = leaseUntil - TimeSpan.FromSeconds(30), LeaseUntil = leaseUntil });
        await context.SaveChangesAsync(Token);
    }

    // The row once its lease runs on the real clock, read every 10 milliseconds: renewed by its holder, or taken over.
    private static async Task<LockRow> LiveRowAsync(FluentCmsTestHost host)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            var row = await RowAsync(host);
            if (row.LeaseUntil > DateTimeOffset.UtcNow)
            {
                return row;
            }

            watch.Elapsed.ShouldBeLessThan(Patience);
            await Task.Delay(TimeSpan.FromMilliseconds(10), Token);
        }
    }

    // The handle of a wait that was canceled, which is null unless the wait took the lock first.
    private static async Task<IDistributedLockHandle?> CanceledAsync(Task<IDistributedLockHandle?> waiting)
    {
        try
        {
            return await waiting;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    // A write transaction on a connection of its own, as the schema apply of another process holds one; disposing it ends it.
    private async Task<SqliteConnection> WriteTransactionAsync()
    {
        var connection = new SqliteConnection($"Data Source={_file};Pooling=False");
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = "BEGIN IMMEDIATE";
        await command.ExecuteNonQueryAsync(Token);
        return connection;
    }

    // Writes of the lock that give up waiting for SQLite's write lock at the first busy timeout, of 5 seconds, rather than
    // retrying it until the command timeout of 30 seconds.
    private static FluentCmsTestHost Impatient(FluentCmsTestHost host) =>
        host.Configure(cms => cms.Services.ConfigureDbContext<SystemDbContext>(options => options.UseSqlite(sqlite => sqlite.CommandTimeout(1))));

    // The first lease_until other than the one given, read every 10 milliseconds, since a renewal signals its UPDATE before it
    // commits.
    private static async Task<DateTimeOffset> NextLeaseAsync(FluentCmsTestHost host, DateTimeOffset? before)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            if ((await RowAsync(host)).LeaseUntil is { } lease && lease != before)
            {
                return lease;
            }

            watch.Elapsed.ShouldBeLessThan(Patience);
            await Task.Delay(TimeSpan.FromMilliseconds(10), Token);
        }
    }

    private static async Task<LockRow> RowAsync(FluentCmsTestHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<LockRow>().AsNoTracking().SingleAsync(row => row.Name == Name, Token);
    }

    private static FluentCmsTestHost Watched(FluentCmsTestHost host, IInterceptor signals) =>
        host.Configure(cms => cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(signals)));

    // Signals a take that found the lock's lease over, or the lock free, as it begins its write transaction: a transaction
    // that begins on the connection that last read the lock's row.
    private sealed class TakeSignals(string name) : IDbCommandInterceptor, IDbTransactionInterceptor
    {
        private DbConnection? _read;

        private int _count;

        public CommandSignals.Signals Begins { get; } = new();

        // Every read of the lock's row, each try's first among them.
        public CommandSignals.Signals Reads { get; } = new();

        public int Count => Volatile.Read(ref _count);

        public ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("\"cms_lock\"", StringComparison.Ordinal) && command.Parameters.Cast<DbParameter>().Any(parameter => Equals(parameter.Value, name)))
            {
                Volatile.Write(ref _read, command.Connection);
                Reads.Add();
            }

            return ValueTask.FromResult(result);
        }

        public ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref _read, null, connection) == connection)
            {
                Interlocked.Increment(ref _count);
                Begins.Add();
            }

            return ValueTask.FromResult(result);
        }
    }

    // The test's clock, whose first timer of the length given is held for the test to expire; the others run on the real
    // clock, as the controllable one's do.
    private sealed class HeldTimers(ControllableTimeProvider clock, TimeSpan length) : TimeProvider
    {
        private readonly TaskCompletionSource<Action> _held = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // Completes, with what expires it, once the timer is set.
        public Task<Action> Held => _held.Task;

        public override TimeZoneInfo LocalTimeZone => clock.LocalTimeZone;

        public override DateTimeOffset GetUtcNow() => clock.GetUtcNow();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime != length || _held.Task.IsCompleted)
            {
                return base.CreateTimer(callback, state, dueTime, period);
            }

            _held.TrySetResult(() => callback(state));
            return new Unscheduled();
        }

        private sealed class Unscheduled : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    // Completes once the lock logs the event given.
    private sealed class LoggedSignal(int id) : ILoggerProvider, ILogger
    {
        private readonly TaskCompletionSource _logged = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Logged => _logged.Task;

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (eventId.Id == id)
            {
                _logged.TrySetResult();
            }
        }

        public void Dispose()
        {
        }
    }

    // Fails the renewals of one lock while a test has it on: the updates of its row, which its release is too.
    private sealed class FailingRenewals(string name) : DbCommandInterceptor
    {
        private volatile bool _on;

        public bool On
        {
            get => _on;
            set => _on = value;
        }

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Fail(command);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Fail(command);
            return ValueTask.FromResult(result);
        }

        private void Fail(DbCommand command)
        {
            if (On && command.CommandText.Contains("UPDATE \"cms_lock\"", StringComparison.Ordinal) && command.Parameters.Cast<DbParameter>().Any(parameter => Equals(parameter.Value, name)))
            {
                throw new InvalidOperationException("The renewal failed.");
            }
        }
    }

    private static void InterlockedMax(ref int target, int value)
    {
        var current = Volatile.Read(ref target);
        while (value > current)
        {
            var seen = Interlocked.CompareExchange(ref target, value, current);
            if (seen == current)
            {
                return;
            }

            current = seen;
        }
    }
}
