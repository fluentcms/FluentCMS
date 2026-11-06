using FluentCMS.Core.Caching;
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
    private readonly FluentCmsTestHost _first;
    private readonly FluentCmsTestHost _second;

    public DistributedLockTests()
    {
        var file = Path.Combine(_directory.FullName, "cms.db");
        _first = new FluentCmsTestHost().UseSqlite(file);
        _second = new FluentCmsTestHost().UseSqlite(file);
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

        var waiting = other.TryAcquireAsync(Name, Patience, Token);
        await Task.Delay(TimeSpan.FromMilliseconds(300), Token);
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
                await Task.Delay(TimeSpan.FromMilliseconds(20), Token);
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
        await using var host = new FluentCmsTestHost();
        var locks = ShortLease(host);
        await using var held = await locks.TryAcquireAsync(Name, TimeSpan.Zero, Token);
        held.ShouldNotBeNull();
        var first = (await RowAsync(host)).LeaseUntil!.Value;

        host.Time.Advance(TimeSpan.FromSeconds(1));
        var renewed = await EventuallyAsync(async () => (await RowAsync(host)).LeaseUntil > first);

        renewed.ShouldBeTrue();
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

        (await EventuallyAsync(() => Task.FromResult(held.Lost.IsCancellationRequested))).ShouldBeTrue();

        await held.DisposeAsync();
        (await RowAsync(host)).Owner.ShouldBe(Crashed);
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

    private static async Task<LockRow> RowAsync(FluentCmsTestHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<LockRow>().AsNoTracking().SingleAsync(row => row.Name == Name, Token);
    }

    private static async Task<bool> EventuallyAsync(Func<Task<bool>> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (await condition())
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), Token);
        }

        return false;
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
