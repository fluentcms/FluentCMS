using System.Data.Common;
using FluentCMS.Core.Storage;
using FluentCMS.Data.PostgreSql;
using FluentCMS.Data.Tests.Providers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Migrations;

// No share of the migrations lock outlives its handle on a pooled connection, and a release that fails leaves the outcome of
// what ran under the lock as it was (#3528).
public sealed class MigrationLockReleaseTests : IDisposable
{
    private const string Production = "Production";

    private const string Because = "a lock of the session, which SQLite's row is not";

    private readonly TestDatabase _database = TestDatabase.Create();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _database.Dispose();

    public enum MigrationStep
    {
        None,
        EfTakesItsLock,
        EfCreatesItsHistoryTable,
        EfWritesItsHistoryRow,
    }

    [Theory]
    [InlineData(MigrationStep.None, false)]
    [InlineData(MigrationStep.EfTakesItsLock, false)]
    [InlineData(MigrationStep.EfTakesItsLock, true)]
    [InlineData(MigrationStep.EfCreatesItsHistoryTable, false)]
    [InlineData(MigrationStep.EfCreatesItsHistoryTable, true)]
    [InlineData(MigrationStep.EfWritesItsHistoryRow, false)]
    [InlineData(MigrationStep.EfWritesItsHistoryRow, true)]
    public async Task NoShareOfTheLockStaysAfterAMigrationSucceedsFailsOrIsCancelled(MigrationStep step, bool cancelled)
    {
        Assert.SkipWhen(TestProvider.IsSqlite, TestProvider.ServerOnlyReason(Because));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var fault = new Fault(step, cancelled ? cancellation : null);
        var logs = new Releases();
        using var host = Host(_database, fault, logs);
        var runner = host.Services.GetRequiredService<MigrationRunner>();

        if (step == MigrationStep.None)
        {
            (await runner.MigrateAsync(cancellation.Token)).ShouldNotBeEmpty();
        }
        else if (cancelled)
        {
            await Should.ThrowAsync<OperationCanceledException>(() => runner.MigrateAsync(cancellation.Token));
            fault.Reached.ShouldBeTrue();
        }
        else
        {
            (await Should.ThrowAsync<InvalidOperationException>(() => runner.MigrateAsync(cancellation.Token))).Message.ShouldBe(Fault.Message);
        }

        logs.Failed.ShouldBeEmpty();
        await MigrationLockSessions.UntilFreeAsync(_database, Token);
    }

    [SqlServerFact("a database that EF creates, whose lock it takes before the runner can")]
    public async Task NoShareStaysWhenEfFailsInADatabaseItCreated()
    {
        var name = $"t_{Guid.NewGuid():N}";
        using var created = new ServerDatabase(name, new SqlConnectionStringBuilder(_database.ConnectionString) { InitialCatalog = name }.ToString());
        using var host = Host(created, new Fault(MigrationStep.EfCreatesItsHistoryTable, null), new Releases());

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => host.Services.GetRequiredService<MigrationRunner>().MigrateAsync(Token));

        exception.Message.ShouldBe(Fault.Message);
        await MigrationLockSessions.UntilFreeAsync(created, Token);
    }

    // The function that PostgreSQL finds first takes the real lock and then waits, which is a grant whose answer the
    // cancellation overtakes.
    [PostgreSqlFact(Because)]
    public async Task ACancellationThatRacesAGrantLeavesTheLockOnNoPooledConnection()
    {
        var provider = Shadowed("pg_advisory_lock", "void", "PERFORM pg_catalog.pg_advisory_lock(key); PERFORM pg_catalog.pg_sleep(300);");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);

        var taking = provider.LockMigrationsAsync(cancellation.Token);
        await MigrationLockSessions.UntilHeldAsync(_database, taking, Token);
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => taking);
        await MigrationLockSessions.UntilFreeAsync(_database, Token);
    }

    [PostgreSqlFact(Because)]
    public async Task AReleaseThatFailsEndsTheSessionThatHoldsTheLockOnPostgreSql()
    {
        var provider = Shadowed("pg_advisory_unlock", "boolean", "RAISE EXCEPTION 'release refused';");
        var held = (await provider.LockMigrationsAsync(Token)).ShouldNotBeNull();
        (await MigrationLockSessions.HoldersAsync(_database, Token)).ShouldHaveSingleItem();

        var exception = await Should.ThrowAsync<PostgresException>(async () => await held.DisposeAsync());

        exception.MessageText.ShouldBe("release refused");
        await MigrationLockSessions.UntilFreeAsync(_database, Token);
    }

    // sp_getapplock cannot be made to answer late, so the session holds a share already when its wait is cancelled, as it
    // does when the grant and the cancellation race: a shared one, which waits to become exclusive beside another's.
    [SqlServerFact(Because)]
    public async Task ACancelledWaitLeavesWhatItsSessionHoldsOnNoPooledConnection()
    {
        const string Share = "EXEC sp_getapplock @Resource = '__EFMigrationsLock', @LockOwner = 'Session', @LockMode = 'Shared';";
        using var host = Host(_database, new Fault(MigrationStep.None, null), new Releases());
        await using var other = MigrationLockSessions.Connect(_database);
        await other.OpenAsync(Token);
        await ExecuteAsync(other, Share);

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
            await context.Database.OpenConnectionAsync(Token);
            await ExecuteAsync(context.Database.GetDbConnection(), Share);

            var taking = host.Services.GetRequiredService<IDataProvider>().TakeMigrationsLockAsync(context, cancellation.Token);
            await MigrationLockSessions.UntilWaitedForAsync(_database, taking, Token);
            await cancellation.CancelAsync();

            await Should.ThrowAsync<Exception>(() => taking);
        }

        await other.CloseAsync();
        await MigrationLockSessions.UntilFreeAsync(_database, Token);
    }

    // A transaction that the release's command does not carry makes the driver refuse it on a session that is well.
    [SqlServerFact(Because)]
    public async Task AReleaseThatFailsEndsTheSessionThatHoldsTheLockOnSqlServer()
    {
        using var host = Host(_database, new Fault(MigrationStep.None, null), new Releases());
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
            var held = (await host.Services.GetRequiredService<IDataProvider>().TakeMigrationsLockAsync(context, Token)).ShouldNotBeNull();
            (await MigrationLockSessions.HoldersAsync(_database, Token)).ShouldHaveSingleItem();
            _ = await context.Database.GetDbConnection().BeginTransactionAsync(Token);

            await Should.ThrowAsync<InvalidOperationException>(async () => await held.DisposeAsync());
        }

        await MigrationLockSessions.UntilFreeAsync(_database, Token);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AReleaseThatFailsIsLoggedAndDoesNotReplaceTheOutcomeOfTheMigration(bool migrationFails)
    {
        var logs = new Releases();
        using var host = Host(_database, new Fault(migrationFails ? MigrationStep.EfWritesItsHistoryRow : MigrationStep.None, null), logs);
        var runner = InterceptedProvider.Runner(host.Services, (member, value) =>
            member is nameof(IDataProvider.LockMigrationsAsync) or nameof(IDataProvider.TakeMigrationsLockAsync)
                ? WithFailingReleaseAsync((Task<IAsyncDisposable?>)value!)
                : value);

        if (migrationFails)
        {
            (await Should.ThrowAsync<InvalidOperationException>(() => runner.MigrateAsync(Token))).Message.ShouldBe(Fault.Message);
        }
        else
        {
            (await runner.MigrateAsync(Token)).ShouldNotBeEmpty();
        }

        logs.Failed.ShouldNotBeEmpty();
        logs.Failed.ShouldAllBe(message => message.StartsWith("The release of a migrations lock failed (IOException);", StringComparison.Ordinal));
    }

    private static async Task<IAsyncDisposable?> WithFailingReleaseAsync(Task<IAsyncDisposable?> taking) => new FailingRelease(await taking);

    private static IHost Host(TestDatabase database, Fault fault, Releases logs) => database.CreateHost(
        ArticleSchemas.NoTypes,
        Production,
        services: services => services.AddSingleton<ILoggerProvider>(logs),
        configure: cms => cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(fault)));

    // A provider whose sessions find a function of the test before PostgreSQL's own of the same name.
    private PostgreSqlDataProvider Shadowed(string function, string returns, string body)
    {
        _database.Execute($"CREATE SCHEMA shadow; CREATE FUNCTION shadow.{function}(key bigint) RETURNS {returns} LANGUAGE plpgsql AS $$ BEGIN {body} END $$;");
        return new PostgreSqlDataProvider(new NpgsqlConnectionStringBuilder(_database.ConnectionString) { SearchPath = "shadow,pg_catalog,public" }.ToString());
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Constants of the tests.
        command.CommandText = sql;
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync(Token);
    }

    private sealed class FailingRelease(IAsyncDisposable? taken) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            if (taken is not null)
            {
                await taken.DisposeAsync();
            }

            throw new IOException("The release failed.");
        }
    }

    // Fails or cancels the system stream's migration at one step of EF's.
    private sealed class Fault(MigrationStep step, CancellationTokenSource? cancellation) : DbCommandInterceptor
    {
        public const string Message = "The test fails the migration here.";

        public bool Reached { get; private set; }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            InterceptAsync(command.CommandText, result);

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default) =>
            InterceptAsync(command.CommandText, result);

        private async ValueTask<T> InterceptAsync<T>(string sql, T result)
        {
            if (!IsTheStep(sql))
            {
                return result;
            }

            Reached = true;
            if (cancellation is null)
            {
                throw new InvalidOperationException(Message);
            }

            await cancellation.CancelAsync();
            throw new OperationCanceledException(cancellation.Token);
        }

        private bool IsTheStep(string sql) => step switch
        {
            MigrationStep.EfTakesItsLock => sql.Contains("sp_getapplock", StringComparison.Ordinal) || sql.Contains("LOCK TABLE", StringComparison.Ordinal),
            MigrationStep.EfCreatesItsHistoryTable => sql.Contains("CREATE TABLE", StringComparison.Ordinal) && sql.Contains(SystemDbContext.HistoryTable, StringComparison.Ordinal),
            MigrationStep.EfWritesItsHistoryRow => sql.Contains("INSERT INTO", StringComparison.Ordinal) && sql.Contains(SystemDbContext.HistoryTable, StringComparison.Ordinal),
            _ => false,
        };
    }

    private sealed class Releases : ILoggerProvider, ILogger
    {
        private readonly List<string> _failed = [];

        public List<string> Failed
        {
            get
            {
                lock (_failed)
                {
                    return [.. _failed];
                }
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
            if (eventId.Id != 9307)
            {
                return;
            }

            lock (_failed)
            {
                _failed.Add(formatter(state, exception));
            }
        }
    }
}
