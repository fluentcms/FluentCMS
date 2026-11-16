using System.Diagnostics;
using FluentCMS.Core.Caching;
using FluentCMS.Core.IntegrationTests.Testing;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Providers;
using FluentCMS.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Caching;

public sealed class DistributedLockTests : IAsyncDisposable
{
    private const string Name = "tests:lock";
    private const string Crashed = "crashed-host/1/0123456789abcdef0123456789abcdef";

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

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
    private static DatabaseDistributedLock ShortLease(FluentCmsTestHost host) => new(
        host.Services.GetRequiredService<IServiceScopeFactory>(),
        host.Time,
        NullLogger<DatabaseDistributedLock>.Instance)
    {
        Lease = TimeSpan.FromMilliseconds(300),
    };

    // A lease of 300 milliseconds on the real clock, renewed every 100, for a test whose time passes in SQLite's waits.
    private static DatabaseDistributedLock RealClockShortLease(FluentCmsTestHost host) => RealClockLease(host, TimeSpan.FromMilliseconds(300));

    private static DatabaseDistributedLock RealClockLease(FluentCmsTestHost host, TimeSpan lease) => new(
        host.Services.GetRequiredService<IServiceScopeFactory>(),
        TimeProvider.System,
        NullLogger<DatabaseDistributedLock>.Instance)
    {
        Lease = lease,
    };

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

    private static FluentCmsTestHost Watched(FluentCmsTestHost host, CommandSignals commands) =>
        host.Configure(cms => cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(commands)));

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
