using System.Data.Common;
using System.Threading.Channels;
using FluentCMS.Core.Commands;
using FluentCMS.Core.Storage;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Data.Tests.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Migrations;

// EF locks a SQLite database for its migrations with the row of __EFMigrationsLock, which a process that is killed while it
// migrates leaves behind, and EF then waits for it without end (#3177).
public sealed class MigrationLockTests : IDisposable
{
    private const string Production = "Production";

    private const string Because = "EF's migrations lock, a row on SQLite only";

    private const string Written = "2026-11-15 10:00:00+00:00";

    private readonly SqliteFileDatabase _database = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _database.Dispose();

    // As EF takes the lock, with a process that never deletes the row.
    private void LeaveLockRow() => _database.Execute($"""
        CREATE TABLE IF NOT EXISTS "__EFMigrationsLock" ("Id" INTEGER NOT NULL CONSTRAINT "PK___EFMigrationsLock" PRIMARY KEY, "Timestamp" TEXT NOT NULL);
        INSERT OR IGNORE INTO "__EFMigrationsLock"("Id", "Timestamp") VALUES(1, '{Written}');
        """);

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

    [SqliteFact(Because)]
    public async Task AStartGivesUpOnALockRowLeftBehindAndNamesIt()
    {
        LeaveLockRow();
        var logs = new Logs();
        using var host = FastHost(null, logs);
        using var bound = Bound();

        var exception = await Should.ThrowAsync<TimeoutException>(host.StartAsync(bound.Token));

        exception.Message.ShouldStartWith($"EF Core's migrations lock, the row of __EFMigrationsLock written at {Written}, was not released within 15 minutes, so the system stream was not migrated;");
        exception.Message.ShouldContain("once no other process is migrating the database, delete the row with DELETE FROM \"__EFMigrationsLock\"");
        logs.Messages(9305).ShouldHaveSingleItem().ShouldStartWith($"The system stream waits for EF Core's migrations lock, the row of __EFMigrationsLock written at {Written};");
        _database.Tables().ShouldNotContain("cms_role");
    }

    [SqliteFact(Because)]
    public async Task MigrateGivesUpOnALockRowLeftBehindAndNamesIt()
    {
        LeaveLockRow();
        using var host = FastHost(new() { ["FluentCms:Database:MigrateOnStartup"] = "false" }, new Logs());
        using var bound = Bound();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CommandRunner.RunAsync(host.Services, [CommandRunner.Prefix, "migrate"], output, error, bound.Token);

        exitCode.ShouldBe(1);
        error.ToString().ShouldStartWith("FCMS3302: 'fluentcms migrate' failed: TimeoutException: ");
        error.ToString().ShouldContain($"the row of __EFMigrationsLock written at {Written}, was not released within 15 minutes");
        _database.Tables().ShouldNotContain("cms_role");
    }

    [SqliteFact(Because)]
    public async Task AStartWaitsForTheRowToGoAndThenMigrates()
    {
        LeaveLockRow();
        var reads = new LockReads();
        using var host = _database.CreateHost(
            ArticleSchemas.Titled,
            Production,
            GeneratedMigrations.CompileFirst(ArticleSchemas.Titled),
            configure: cms => cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(reads)));
        using var bound = Bound();

        var start = host.StartAsync(bound.Token);

        // Two reads of the row: the start found the lock held, waited, and looked again.
        await reads.NextAsync(2);
        start.IsCompleted.ShouldBeFalse();
        _database.Execute("DELETE FROM \"__EFMigrationsLock\"");

        await start;

        _database.Tables().ShouldContain("cms_role");
        _database.ReadStrings("select count(*) from \"__EFMigrationsLock\"").ShouldBe(["0"]);
        await host.StopAsync(Token);
    }

    // The runner reads what is pending first, and takes the lock only for a stream that has something to apply.
    [SqliteFact(Because)]
    public async Task AStartWithNothingPendingLeavesTheRowAlone()
    {
        var migrations = GeneratedMigrations.CompileFirst(ArticleSchemas.Titled);
        using (var first = _database.CreateHost(ArticleSchemas.Titled, Production, migrations))
        {
            await first.StartAsync(Token);
            await first.StopAsync(Token);
        }

        LeaveLockRow();
        using var host = _database.CreateHost(ArticleSchemas.Titled, Production, migrations);
        using var bound = Bound();

        await host.StartAsync(bound.Token);

        _database.ReadStrings("select \"Timestamp\" from \"__EFMigrationsLock\"").ShouldBe([Written]);
        await host.StopAsync(Token);
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

    // Signals each read of the lock's row, which a start waiting for the lock repeats while the row is there.
    private sealed class LockReads : DbCommandInterceptor
    {
        private readonly Channel<bool> _reads = Channel.CreateUnbounded<bool>();

        public async Task NextAsync(int count)
        {
            for (var read = 0; read < count; read++)
            {
                await _reads.Reader.ReadAsync(Token).AsTask().WaitAsync(TimeSpan.FromSeconds(30), Token);
            }
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"__EFMigrationsLock\"", StringComparison.Ordinal))
            {
                _reads.Writer.TryWrite(true);
            }

            return ValueTask.FromResult(result);
        }
    }
}
