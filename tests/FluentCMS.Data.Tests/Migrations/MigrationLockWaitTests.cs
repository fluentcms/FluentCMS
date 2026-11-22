using System.Data.Common;
using FluentCMS.Core.Commands;
using FluentCMS.Core.Storage;
using FluentCMS.Data.Tests.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Migrations;

// A process that migrates while another holds the migrations lock waits for it, logs the wait once and gives up with an
// error that names the lock, on every provider. On SQL Server EF's own wait ended at the command timeout and named
// nothing (#3192); on PostgreSQL the lock of the whole run had no bound.
public sealed class MigrationLockWaitTests : IDisposable
{
    private const string Production = "Production";

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

    [Fact]
    public async Task AStartGivesUpOnALockHeldPastTheBoundAndNamesIt()
    {
        var logs = new Logs();
        using var host = FastHost(null, logs);
        await using var held = await HoldAsync(host);
        using var bound = Bound();

        var exception = await Should.ThrowAsync<TimeoutException>(host.StartAsync(bound.Token));

        exception.Message.ShouldStartWith(Held);
        exception.Message.ShouldContain(", was not released within 15 minutes, so the system stream was not migrated; ");
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
        error.ToString().ShouldContain(", was not released within 15 minutes, so the system stream was not migrated; ");
        _database.Tables().ShouldNotContain("cms_role");
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
