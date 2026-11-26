using System.Data.Common;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Jobs;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Plugins;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Plugins;

// IPluginStore<TPlugin> on the provider of the run (#3606): a write is conditional on the version read, so of writers that
// race on one version exactly one wins, and each plugin of the host has values of its own.
public sealed class PluginStoreTests : IDisposable
{
    private const string Key = "job.cursor";

    private static readonly DateTimeOffset Start = new(2026, 11, 26, 9, 0, 0, TimeSpan.Zero);
    private static readonly string[] TwoValues = ["first", "second"];

    private readonly TestDatabase _database = TestDatabase.Create(forSystemTables: true);
    private readonly ManualTimeProvider _clock = new(Start);
    private readonly Statements _statements = new();
    private readonly List<IServiceScope> _scopes = [];
    private readonly ServiceProvider _services;

    public PluginStoreTests()
    {
        try
        {
            _services = _database.CreateServices(configure: cms =>
            {
                cms.Services.AddSingleton<TimeProvider>(_clock);
                cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(_statements));
                cms.AddPlugin<StoreAlphaPlugin>().AddPlugin<StoreBetaPlugin>();
            });
            using var scope = _services.CreateScope();
            _database.CreateSystemTables(scope.ServiceProvider.GetRequiredService<SystemDbContext>());
        }
        catch
        {
            // xunit disposes no test class whose constructor throws, and the database would outlive the test.
            Dispose();
            throw;
        }
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        foreach (var scope in _scopes)
        {
            scope.Dispose();
        }

        _services?.Dispose();
        _database.Dispose();
    }

    [Fact]
    public async Task AValueIsAddedOnceAndWrittenAndDeletedOnlyAtTheVersionRead()
    {
        await using var scope = _services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IPluginStore<StoreAlphaPlugin>>();

        var added = (await store.TryAddAsync(Key, "", Token)).ShouldNotBeNull();
        var again = await store.TryAddAsync(Key, "other", Token);
        _clock.Advance(TimeSpan.FromMinutes(5));
        var set = await store.TrySetAsync(Key, "0192e0a0-0000-7000-8000-000000000001", added.Version, Token);
        var stale = await store.TrySetAsync(Key, "lost", added.Version, Token);
        var read = await store.GetAsync(Key, Token);
        var staleDelete = await store.DeleteAsync(Key, added.Version, Token);
        var deleted = await store.DeleteAsync(Key, added.Version + 1, Token);

        added.ShouldSatisfyAllConditions(
            value => value.Key.ShouldBe(Key),
            value => value.Value.ShouldBe(""),
            value => value.Version.ShouldBeInRange(1, (1L << 62) - 1),
            value => value.UpdatedAt.ShouldBe(Start));
        again.ShouldBeNull();
        set.ShouldBe(new PluginStoreValue(Key, "0192e0a0-0000-7000-8000-000000000001", added.Version + 1, Start.AddMinutes(5)));
        stale.ShouldBeNull();
        read.ShouldNotBeNull().ShouldSatisfyAllConditions(
            value => value.Value.ShouldBe("0192e0a0-0000-7000-8000-000000000001"),
            value => value.Version.ShouldBe(added.Version + 1),
            value => value.UpdatedAt.ShouldBe(Start.AddMinutes(5)));
        staleDelete.ShouldBeFalse();
        deleted.ShouldBeTrue();
        (await store.GetAsync(Key, Token)).ShouldBeNull();
        (await store.TrySetAsync(Key, "gone", added.Version + 1, Token)).ShouldBeNull();
        (await store.DeleteAsync(Key, cancellationToken: Token)).ShouldBeFalse();
    }

    [Fact]
    public async Task ADeleteWithoutAVersionRemovesTheValueAtAnyVersion()
    {
        var store = Store<StoreAlphaPlugin>();
        var added = (await store.TryAddAsync(Key, "a", Token)).ShouldNotBeNull();
        (await store.TrySetAsync(Key, "b", added.Version, Token)).ShouldNotBeNull();

        (await store.DeleteAsync(Key, cancellationToken: Token)).ShouldBeTrue();
        (await store.GetAsync(Key, Token)).ShouldBeNull();
        (await store.TryAddAsync(Key, "c", Token)).ShouldNotBeNull().Value.ShouldBe("c");
    }

    // A writer that read the value, another that deletes it to release it and adds it again to claim it: the first writer's
    // write of the version it read must lose, or both would be told they won.
    [Fact]
    public async Task AWriteOfAVersionReadBeforeADeleteAndAnAddLoses()
    {
        var reader = Store<StoreAlphaPlugin>();
        var other = Store<StoreAlphaPlugin>();
        await reader.TryAddAsync(Key, "old", Token);
        var read = (await reader.GetAsync(Key, Token)).ShouldNotBeNull();

        (await other.DeleteAsync(Key, read.Version, Token)).ShouldBeTrue();
        var claimed = (await other.TryAddAsync(Key, "new", Token)).ShouldNotBeNull();
        var stale = await reader.TrySetAsync(Key, "stale", read.Version, Token);

        stale.ShouldBeNull();
        claimed.Version.ShouldNotBe(read.Version);
        (await reader.GetAsync(Key, Token)).ShouldNotBeNull().ShouldSatisfyAllConditions(
            value => value.Value.ShouldBe("new"),
            value => value.Version.ShouldBe(claimed.Version));
    }

    [Fact]
    public async Task TwoAddsOfOneKeyAfterADeleteStartAtDifferentVersions()
    {
        var store = Store<StoreAlphaPlugin>();

        var first = (await store.TryAddAsync(Key, "first", Token)).ShouldNotBeNull();
        (await store.DeleteAsync(Key, cancellationToken: Token)).ShouldBeTrue();
        var second = (await store.TryAddAsync(Key, "second", Token)).ShouldNotBeNull();

        second.Version.ShouldNotBe(first.Version);
    }

    // Both writers read the version of the add and write it at once, each on a scope of its own as two instances would.
    [Fact]
    public async Task OfTwoWritersOfOneVersionExactlyOneWins()
    {
        var start = (await Store<StoreAlphaPlugin>().TryAddAsync(Key, "start", Token)).ShouldNotBeNull().Version;
        var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var writers = TwoValues.Select(value => WriteAtOnceAsync(go.Task, value)).ToArray();
        go.SetResult();
        var results = await Task.WhenAll(writers);

        results.Count(result => result is not null).ShouldBe(1);
        var winner = results.Single(result => result is not null)!;
        winner.Version.ShouldBe(start + 1);
        (await Store<StoreAlphaPlugin>().GetAsync(Key, Token)).ShouldNotBeNull().ShouldSatisfyAllConditions(
            value => value.Value.ShouldBe(winner.Value),
            value => value.Version.ShouldBe(start + 1));

        async Task<PluginStoreValue?> WriteAtOnceAsync(Task go, string value)
        {
            await using var scope = _services.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IPluginStore<StoreAlphaPlugin>>();
            await go;
            return await store.TrySetAsync(Key, value, start, Token);
        }
    }

    // Writers that read, add one and write, and read again whenever they lose, count every increment once.
    [Fact]
    public async Task WritersThatRetryWhenTheyLoseLoseNoIncrement()
    {
        const int Writers = 4;
        const int Increments = 10;
        var start = (await Store<StoreAlphaPlugin>().TryAddAsync(Key, "0", Token)).ShouldNotBeNull().Version;
        var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var writers = Enumerable.Range(0, Writers).Select(_ => IncrementAsync(go.Task)).ToArray();
        go.SetResult();
        await Task.WhenAll(writers);

        var value = await Store<StoreAlphaPlugin>().GetAsync(Key, Token);
        value.ShouldNotBeNull().Value.ShouldBe((Writers * Increments).ToString(System.Globalization.CultureInfo.InvariantCulture));
        value.Version.ShouldBe(start + (Writers * Increments));

        async Task IncrementAsync(Task gate)
        {
            await gate;
            for (var done = 0; done < Increments;)
            {
                await using var scope = _services.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<IPluginStore<StoreAlphaPlugin>>();
                var read = (await store.GetAsync(Key, Token))!;
                var next = (int.Parse(read.Value, System.Globalization.CultureInfo.InvariantCulture) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (await store.TrySetAsync(Key, next, read.Version, Token) is not null)
                {
                    done++;
                }
            }
        }
    }

    [Fact]
    public async Task OfTwoAddsOfOneKeyAtOnceExactlyOneAdds()
    {
        var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var adders = TwoValues.Select(value => AddAtOnceAsync(go.Task, value)).ToArray();
        go.SetResult();
        var results = await Task.WhenAll(adders);

        results.Count(result => result is not null).ShouldBe(1);
        var winner = results.Single(result => result is not null)!;
        (await Store<StoreAlphaPlugin>().GetAsync(Key, Token)).ShouldNotBeNull().Value.ShouldBe(winner.Value);

        async Task<PluginStoreValue?> AddAtOnceAsync(Task start, string value)
        {
            await using var scope = _services.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IPluginStore<StoreAlphaPlugin>>();
            await start;
            return await store.TryAddAsync(Key, value, Token);
        }
    }

    [Fact]
    public async Task EachPluginOfTheHostHasValuesOfItsOwn()
    {
        var alpha = Store<StoreAlphaPlugin>();
        var beta = Store<StoreBetaPlugin>();

        var alphaAdded = (await alpha.TryAddAsync(Key, "alpha", Token)).ShouldNotBeNull();
        var betaAdded = (await beta.TryAddAsync(Key, "beta", Token)).ShouldNotBeNull();
        (await alpha.TrySetAsync(Key, "alpha again", alphaAdded.Version, Token)).ShouldNotBeNull();
        var alphaDeleted = await alpha.DeleteAsync(Key, cancellationToken: Token);

        alphaDeleted.ShouldBeTrue();
        (await alpha.GetAsync(Key, Token)).ShouldBeNull();
        (await beta.GetAsync(Key, Token)).ShouldNotBeNull().ShouldSatisfyAllConditions(
            value => value.Value.ShouldBe("beta"),
            value => value.Version.ShouldBe(betaAdded.Version));
        await using var scope = _services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<PluginStoreRow>().AsNoTracking().Select(row => row.Plugin + "/" + row.Key).ToListAsync(Token))
            .ShouldBe(["store-beta/" + Key]);
    }

    [Fact]
    public void AStoreForATypeThatIsNoPluginOfTheHostIsRefused()
    {
        using var scope = _services.CreateScope();

        Should.Throw<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IPluginStore<StoreGammaPlugin>>())
            .Message.ShouldContain(typeof(StoreGammaPlugin).FullName!);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Cursor")]
    [InlineData("job..cursor")]
    [InlineData(".cursor")]
    [InlineData("cursor.")]
    [InlineData("job cursor")]
    [InlineData("cursor\n")]
    [InlineData("cursör")]
    [InlineData("job/cursor")]
    public async Task AKeyOutsideTheRuleIsRefusedBeforeAnyStatement(string key)
    {
        var store = Store<StoreAlphaPlugin>();
        var before = _statements.Count;

        await Should.ThrowAsync<ArgumentException>(() => store.GetAsync(key, Token));
        await Should.ThrowAsync<ArgumentException>(() => store.TryAddAsync(key, "v", Token));
        await Should.ThrowAsync<ArgumentException>(() => store.TrySetAsync(key, "v", 1, Token));
        await Should.ThrowAsync<ArgumentException>(() => store.DeleteAsync(key, cancellationToken: Token));

        _statements.Count.ShouldBe(before);
    }

    [Fact]
    public async Task TheLongestKeyAndValueAreKeptAndALongerOneIsRefusedBeforeAnyStatement()
    {
        var store = Store<StoreAlphaPlugin>();
        var key = string.Join('.', Enumerable.Repeat("abcdefg", 16));
        var value = new string('é', PluginStoreRow.ValueMaxLength);
        key.Length.ShouldBe(PluginStoreRow.KeyMaxLength - 1);
        key += "h";

        (await store.TryAddAsync(key, value, Token)).ShouldNotBeNull();
        (await store.GetAsync(key, Token)).ShouldNotBeNull().Value.ShouldBe(value);

        var before = _statements.Count;
        await Should.ThrowAsync<ArgumentException>(() => store.GetAsync(key + "h", Token));
        await Should.ThrowAsync<ArgumentException>(() => store.TryAddAsync(Key, value + "x", Token));
        await Should.ThrowAsync<ArgumentException>(() => store.TrySetAsync(key, value + "x", 1, Token));
        await Should.ThrowAsync<ArgumentNullException>(() => store.TryAddAsync(Key, null!, Token));
        await Should.ThrowAsync<ArgumentException>(() => store.TryAddAsync(Key, "a\0b", Token));
        await Should.ThrowAsync<ArgumentException>(() => store.TrySetAsync(key, "\0", 1, Token));
        _statements.Count.ShouldBe(before);
    }

    // Each call commits on its own, so inside a transaction of the caller it would outlive a rollback.
    [Fact]
    public async Task ACallInsideATransactionOfTheCallerIsRefused()
    {
        await using var scope = _services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IPluginStore<StoreAlphaPlugin>>();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();

        await using (var transaction = await context.Database.BeginTransactionAsync(Token))
        {
            await Should.ThrowAsync<InvalidOperationException>(() => store.TryAddAsync(Key, "in", Token));
            await Should.ThrowAsync<InvalidOperationException>(() => store.GetAsync(Key, Token));
            await transaction.RollbackAsync(Token);
        }

        (await store.TryAddAsync(Key, "after", Token)).ShouldNotBeNull();
    }

    private IPluginStore<TPlugin> Store<TPlugin>()
        where TPlugin : class, IPlugin
    {
        var scope = _services.CreateScope();
        _scopes.Add(scope);
        return scope.ServiceProvider.GetRequiredService<IPluginStore<TPlugin>>();
    }

    // Counts the statements the system context sends.
    private sealed class Statements : DbCommandInterceptor
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Interlocked.Increment(ref _count);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Interlocked.Increment(ref _count);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return ValueTask.FromResult(result);
        }
    }
}

[FluentCmsPlugin("store-alpha", SdkRange = "[1.0.0,2.0.0)")]
public sealed class StoreAlphaPlugin : IPlugin
{
    public void ConfigureServices(IPluginServiceBuilder plugin)
    {
    }

    public void Configure(IPluginAppBuilder app)
    {
    }
}

[FluentCmsPlugin("store-beta", SdkRange = "[1.0.0,2.0.0)")]
public sealed class StoreBetaPlugin : IPlugin
{
    public void ConfigureServices(IPluginServiceBuilder plugin)
    {
    }

    public void Configure(IPluginAppBuilder app)
    {
    }
}

// Never added to a host.
[FluentCmsPlugin("store-gamma", SdkRange = "[1.0.0,2.0.0)")]
public sealed class StoreGammaPlugin : IPlugin
{
    public void ConfigureServices(IPluginServiceBuilder plugin)
    {
    }

    public void Configure(IPluginAppBuilder app)
    {
    }
}
