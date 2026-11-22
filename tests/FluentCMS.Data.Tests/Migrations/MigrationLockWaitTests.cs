using System.Data.Common;
using FluentCMS.Core.Commands;
using FluentCMS.Core.Storage;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Plugins;
using FluentCMS.Plugins.Media;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Migrations;

// A process that migrates while another holds the migrations lock waits for it, logs the wait once and gives up with an
// error that names the lock, on every provider. On SQL Server EF's own wait ended at the command timeout and named
// nothing (#3192); on PostgreSQL the lock of the whole run had no bound. It takes the lock before its checks read what a
// running migration locks (#3528).
public sealed class MigrationLockWaitTests : IDisposable
{
    private const string Production = "Production";

    private const int ShortCommandTimeoutSeconds = 3;

    private readonly TestDatabase _database = TestDatabase.Create();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _database.Dispose();

    // The lock as the waiter names it.
    private static string Held => TestProvider.Name switch
    {
        TestProvider.Sqlite => "EF Core's migrations lock, the row of __EFMigrationsLock written at ",
        TestProvider.PostgreSql => "the migrations lock of FluentCMS, which another session of the database holds",
        _ => "EF Core's migrations lock, the application lock __EFMigrationsLock that another session holds",
    };

    // A lock of the session is waited for before the run has looked at any stream.
    private static string NotMigrated => TestProvider.IsSqlite ? "the system stream was not migrated" : "this run migrated no stream";

    // The same database, through connections whose commands give up soon. Not disposed, since the database is _database's.
    private TestDatabase WithShortCommandTimeout() => TestProvider.Name switch
    {
        TestProvider.Sqlite => _database,
        TestProvider.PostgreSql => new ServerDatabase("", new NpgsqlConnectionStringBuilder(_database.ConnectionString) { CommandTimeout = ShortCommandTimeoutSeconds }.ToString()),
        _ => new ServerDatabase("", new SqlConnectionStringBuilder(_database.ConnectionString) { CommandTimeout = ShortCommandTimeoutSeconds }.ToString()),
    };

    // Another process's lock, held until disposed: the lock of the whole run where the provider takes one, and otherwise
    // EF's own, through EF itself, on a connection of its own.
    private static async Task<IAsyncDisposable> HoldAsync(IHost host)
    {
        if (await host.Services.GetRequiredService<IDataProvider>().LockMigrationsAsync(Token) is { } run)
        {
            return run;
        }

        var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        await context.Database.OpenConnectionAsync(Token);
        return new EfLock(await context.GetService<IHistoryRepository>().AcquireDatabaseLockAsync(Token), scope);
    }

    // A bound of its own, so that a wait without end fails the test as a cancellation rather than hanging it.
    private static CancellationTokenSource Bound()
    {
        var bound = CancellationTokenSource.CreateLinkedTokenSource(Token);
        bound.CancelAfter(TimeSpan.FromSeconds(60));
        return bound;
    }

    // The runner's monotonic clock runs a thousand times fast, so the 15 minutes of its wait pass within a second or two.
    private IHost FastHost(Dictionary<string, string?>? settings, Logs logs)
    {
        var clock = new FastMonotonicClock(null);
        return _database.CreateHost(ArticleSchemas.Titled, Production, GeneratedMigrations.CompileFirst(ArticleSchemas.Titled), settings, services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton<ILoggerProvider>(logs);
        });
    }

    [Fact]
    public async Task AStartWaitsPastTheCommandTimeoutForAnotherProcessAndThenMigrates()
    {
        using var host = _database.CreateHost(
            ArticleSchemas.Titled,
            Production,
            GeneratedMigrations.CompileFirst(ArticleSchemas.Titled),
            configure: cms => cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(new ShortLockTimeout())));
        var held = await HoldAsync(host);
        using var bound = Bound();

        var start = host.StartAsync(bound.Token);

        // Three times the timeout that EF's own request for the lock has here.
        if (await Task.WhenAny(start, Task.Delay(TimeSpan.FromSeconds(3), Token)) == start)
        {
            await start;
        }

        start.IsCompleted.ShouldBeFalse();
        await held.DisposeAsync();

        await start;

        _database.Tables().ShouldContain("cms_role");
        await host.StopAsync(Token);
    }

    // The media stream's open transaction holds what a start's checks read: its history table on PostgreSQL and, since the
    // system stream is at its baseline, the catalog on SQL Server. A bare lock on an empty database holds neither.
    [Fact]
    public async Task ASecondStartWaitsPastTheCommandTimeoutForAMigrationWhoseDdlIsOpenAndThenStarts()
    {
        using (var built = _database.CreateHost(ArticleSchemas.NoTypes, Production))
        {
            await built.StartAsync(Token);
            await built.StopAsync(Token);
        }

        var ddl = new OpenDdl();
        using var migrator = _database.CreateHost(ArticleSchemas.NoTypes, Production, configure: cms =>
        {
            cms.AddPlugin<MediaPlugin>();
            cms.Services.ConfigureDbContext<PluginDbContext<MediaPlugin>>(options => options.AddInterceptors(ddl));
        });
        using var second = WithShortCommandTimeout().CreateHost(ArticleSchemas.NoTypes, Production, configure: cms => cms.AddPlugin<MediaPlugin>());
        using var bound = Bound();

        var migrating = migrator.Services.GetRequiredService<MigrationRunner>().MigrateAsync(bound.Token);
        try
        {
            if (await Task.WhenAny(migrating, ddl.IsOpen.WaitAsync(bound.Token)) == migrating)
            {
                await migrating;
            }

            ddl.IsOpen.IsCompletedSuccessfully.ShouldBeTrue();
            var start = second.StartAsync(bound.Token);
            if (!TestProvider.IsSqlite)
            {
                await MigrationLockSessions.UntilWaitedForAsync(_database, start, bound.Token);
            }

            // The transaction ends only at the signal; the time is what takes the wait past the timeout of a command.
            if (await Task.WhenAny(start, Task.Delay(TimeSpan.FromSeconds(ShortCommandTimeoutSeconds + 2), bound.Token)) == start)
            {
                await start;
            }

            start.IsCompleted.ShouldBeFalse();
            ddl.Commit();
            await migrating;
            await start;
        }
        finally
        {
            ddl.Commit();
            await Task.WhenAny(migrating);
        }

        _database.Tables().ShouldContain("cms_media_preset");
        await second.StopAsync(Token);
    }

    [Fact]
    public async Task TheDocumentedQuerySelectsTheHolderOnly()
    {
        Assert.SkipWhen(TestProvider.IsSqlite, TestProvider.ServerOnlyReason("the operator queries of a lock of the session"));
        using var elsewhere = TestDatabase.Create();
        using var otherHost = elsewhere.CreateHost(ArticleSchemas.NoTypes, Production);
        using var host = _database.CreateHost(ArticleSchemas.NoTypes, Production);
        using var waiter = _database.CreateHost(ArticleSchemas.NoTypes, Production);
        using var bound = Bound();

        var held = await HoldAsync(host);
        var holder = (await MigrationLockSessions.HoldersAsync(_database, bound.Token)).ShouldHaveSingleItem();

        await using var inAnotherDatabase = await HoldAsync(otherHost);
        await using var ofAnotherKey = MigrationLockSessions.Connect(_database);
        await ofAnotherKey.OpenAsync(bound.Token);
        await using (var command = ofAnotherKey.CreateCommand())
        {
            command.CommandText = TestProvider.IsPostgreSql
                ? "SELECT pg_advisory_lock(42)"
                : "EXEC sp_getapplock @Resource = '__EFMigrationsLockOfAnother', @LockOwner = 'Session', @LockMode = 'Exclusive'";
            await command.ExecuteNonQueryAsync(bound.Token);
        }

        var waiting = waiter.Services.GetRequiredService<MigrationRunner>().MigrateAsync(bound.Token);
        try
        {
            await MigrationLockSessions.UntilWaitedForAsync(_database, waiting, bound.Token);

            (await MigrationLockSessions.HoldersAsync(_database, bound.Token)).ShouldBe([holder]);
        }
        finally
        {
            await held.DisposeAsync();
            await Task.WhenAny(waiting);
        }

        await waiting;
    }

    // The checks use two connections, and turning READ_COMMITTED_SNAPSHOT on ends the one that waits in the pool.
    [SqlServerFact("a first start where the baseline turns READ_COMMITTED_SNAPSHOT on")]
    public async Task AFirstStartThatEndsTheOtherSessionsOfTheDatabaseStillStarts()
    {
        _database.Execute("ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT OFF WITH ROLLBACK IMMEDIATE");
        try
        {
            using var host = _database.CreateHost(ArticleSchemas.NoTypes, Production);

            await host.StartAsync(Token);

            _database.Tables().ShouldContain("cms_role");
            await host.StopAsync(Token);
        }
        finally
        {
            await using var connection = MigrationLockSessions.Connect(_database);
            await connection.OpenAsync(Token);
            await using var command = connection.CreateCommand();
            command.CommandText = "IF (SELECT is_read_committed_snapshot_on FROM sys.databases WHERE database_id = DB_ID()) = 0 ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE";
            await command.ExecuteNonQueryAsync(Token);
        }
    }

    [Fact]
    public async Task AStartGivesUpOnALockHeldPastTheBoundAndNamesIt()
    {
        var logs = new Logs();
        using var host = FastHost(null, logs);
        await using var held = await HoldAsync(host);
        using var bound = Bound();

        var exception = await Should.ThrowAsync<TimeoutException>(host.StartAsync(bound.Token));

        exception.Message.ShouldStartWith(Held);
        exception.Message.ShouldContain($", was not released within 15 minutes, so {NotMigrated}; ");
        logs.Messages(9305).ShouldHaveSingleItem().ShouldStartWith($"The system stream waits for {Held}");
        if (!TestProvider.IsSqlite)
        {
            exception.Message.ShouldContain("; another process is applying migrations: a host that starts or a 'fluentcms migrate' holds this lock");
        }

        _database.Tables().ShouldNotContain("cms_role");
    }

    [Fact]
    public async Task MigrateGivesUpOnALockHeldPastTheBoundAndNamesIt()
    {
        using var host = FastHost(new() { ["FluentCms:Database:MigrateOnStartup"] = "false" }, new Logs());
        await using var held = await HoldAsync(host);
        using var bound = Bound();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CommandRunner.RunAsync(host.Services, [CommandRunner.Prefix, "migrate"], output, error, bound.Token);

        exitCode.ShouldBe(1);
        error.ToString().ShouldStartWith($"FCMS3302: 'fluentcms migrate' failed: TimeoutException: {Held}");
        error.ToString().ShouldContain($", was not released within 15 minutes, so {NotMigrated}; ");
        _database.Tables().ShouldNotContain("cms_role");
    }

    // Holds the media stream's transaction open once its DDL has run, at the write of its history row, until Commit.
    private sealed class OpenDdl : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _commit = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task IsOpen => _open.Task;

        public void Commit() => _commit.TrySetResult();

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO", StringComparison.Ordinal) && command.CommandText.Contains("cms_media__migrations", StringComparison.Ordinal))
            {
                _open.TrySetResult();
                await _commit.Task.WaitAsync(cancellationToken);
            }

            return result;
        }
    }

    private sealed class EfLock(IMigrationsDatabaseLock efLock, AsyncServiceScope scope) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await efLock.DisposeAsync();
            await scope.DisposeAsync();
        }
    }

    // EF's request for its lock on SQL Server, with a timeout of one second, which a wait inside it would end at.
    private sealed class ShortLockTimeout : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("sp_getapplock", StringComparison.Ordinal))
            {
                command.CommandTimeout = 1;
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class Logs : ILoggerProvider, ILogger
    {
        private readonly List<(int EventId, string Message)> _messages = [];

        public List<string> Messages(int eventId)
        {
            lock (_messages)
            {
                return [.. _messages.Where(message => message.EventId == eventId).Select(message => message.Message)];
            }
        }

        public ILogger CreateLogger(string categoryName) => this;

        public void Dispose()
        {
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_messages)
            {
                _messages.Add((eventId.Id, formatter(state, exception)));
            }
        }
    }
}
