using System.Data.Common;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Providers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Sqlite;

// The rollback journal of a host that opts out of WAL, where a writer needs a moment with no reader to commit.
public sealed class RollbackJournalWriteContentionTests() : WriteContentionTests(writeAheadLog: false);

// The provider's default, where readers do not hold the writer back.
public sealed class WalWriteContentionTests() : WriteContentionTests(writeAheadLog: true);

public abstract class WriteContentionTests : IDisposable
{
    private const int CommandTimeoutSeconds = 3;

    private readonly SqliteFileDatabase _database;
    private readonly ServiceProvider _services;
    private readonly Commits _commits = new();

    protected WriteContentionTests(bool writeAheadLog)
    {
        _database = new SqliteFileDatabase(writeAheadLog);
        _services = _database.CreateServices(configure: cms => cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(_commits)));
        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<SystemDbContext>().Database.Migrate();

        // EF creates a new database file in WAL mode.
        if (!writeAheadLog)
        {
            _database.Execute("PRAGMA journal_mode = delete");
        }

        _database.ReadStrings("PRAGMA journal_mode").ShouldBe([writeAheadLog ? "wal" : "delete"]);
    }

    public void Dispose()
    {
        _services.Dispose();
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    [SqliteFact("SQLite's file locking")]
    public async Task ASaveWaitsForAReaderInsteadOfFailing()
    {
        using var reader = new SqliteConnection(_database.ConnectionString);
        reader.Open();
        using var transaction = reader.BeginTransaction(deferred: true);
        using (var command = reader.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT count(*) FROM cms_job";
            command.ExecuteScalar();
        }

        // The reader lets go once the save is committing, which in the rollback journal waits for it.
        var release = Task.Run(async () =>
        {
            await _commits.Committing.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            transaction.Commit();
        }, TestContext.Current.CancellationToken);

        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        var site = await context.Set<Site>().SingleAsync(row => row.IsDefault, TestContext.Current.CancellationToken);
        site.Name = "Renamed";

        await Should.NotThrowAsync(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        await release;
        _database.ReadStrings("select name from cms_site where is_default = 1").ShouldBe(["Renamed"]);
    }

    [SqliteFact("SQLite's file locking")]
    public async Task ASaveIsNotStarvedByReadersThatKeepComing()
    {
        using var readers = new Readers(_database.ConnectionString);

        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        context.Database.SetCommandTimeout(CommandTimeoutSeconds);
        context.Add(new Site { Id = Guid.CreateVersion7(), Name = "Second", Slug = "second", DefaultLocale = "en", CreatedAt = DateTimeOffset.UtcNow });

        await Should.NotThrowAsync(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        _database.ReadStrings("select slug from cms_site where slug = 'second'").ShouldHaveSingleItem();
    }

    [SqliteFact("SQLite's file locking")]
    public async Task ABulkWriteIsNotStarvedByReadersThatKeepComing()
    {
        using var readers = new Readers(_database.ConnectionString);

        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        context.Database.SetCommandTimeout(CommandTimeoutSeconds);

        var renamed = 0;
        await Should.NotThrowAsync(async () => renamed = await context.Set<Site>()
            .Where(site => site.IsDefault)
            .ExecuteUpdateAsync(set => set.SetProperty(site => site.Name, "Renamed"), TestContext.Current.CancellationToken));

        renamed.ShouldBe(1);
        _database.ReadStrings("select name from cms_site where is_default = 1").ShouldBe(["Renamed"]);
    }

    private sealed class Commits : DbTransactionInterceptor
    {
        private readonly TaskCompletionSource _committing = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Committing => _committing.Task;

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            _committing.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }

    // Two readers, each holding the database for 100 ms and starting 50 ms after the other, so that there is always one.
    // These waits are the load under test: Microsoft.Data.Sqlite retries a busy statement on the real clock, so readers that
    // keep coming have to hold the database for real time.
    private sealed class Readers : IDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly CountdownEvent _reading = new(2);
        private readonly Task[] _loops;

        public Readers(string connectionString)
        {
            _loops = [Loop(connectionString, TimeSpan.Zero), Loop(connectionString, TimeSpan.FromMilliseconds(50))];
            try
            {
                _reading.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken).ShouldBeTrue();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            Task.WaitAll(_loops.Where(loop => !loop.IsFaulted).ToArray());
            _stop.Dispose();
            _reading.Dispose();
        }

        private Task Loop(string connectionString, TimeSpan offset) => Task.Run(async () =>
        {
            await Task.Delay(offset);
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            var first = true;
            while (!_stop.IsCancellationRequested)
            {
                using var transaction = connection.BeginTransaction(deferred: true);
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "SELECT count(*) FROM cms_job";
                command.ExecuteScalar();
                if (first)
                {
                    first = false;
                    _reading.Signal();
                }

                await Task.Delay(100);
                transaction.Commit();
            }
        });
    }
}
