using FluentCMS.Core.Storage;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Providers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Sqlite;

// The rollback journal of a host that opts out of WAL, where a writer that commits or spills its cache keeps readers out,
// so the read of a lock's row waits for it as a write does. Its commands give up after a second.
public sealed class RollbackJournalLockTests : IDisposable
{
    private const string Name = "tests:lock";

    private readonly SqliteFileDatabase _database = new(writeAheadLog: false);
    private readonly ServiceProvider _services;

    public RollbackJournalLockTests()
    {
        _services = _database.CreateServices(configure: cms => cms.Services.ConfigureDbContext<SystemDbContext>(options => options.UseSqlite(sqlite => sqlite.CommandTimeout(1))));
        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<SystemDbContext>().Database.Migrate();

        // EF creates a new database file in WAL mode.
        _database.Execute("PRAGMA journal_mode = delete");
    }

    public void Dispose()
    {
        _services.Dispose();
        _database.Dispose();
    }

    [SqliteFact("SQLite's file locking")]
    public async Task AWaiterWhoseReadOfTheRowWaitsEndsWithItsTimeout()
    {
        var token = TestContext.Current.CancellationToken;
        var locks = _services.GetRequiredService<IDistributedLock>();

        await using (var writer = new SqliteConnection(_database.ConnectionString))
        {
            await writer.OpenAsync(token);
            await using var command = writer.CreateCommand();
            command.CommandText = "BEGIN EXCLUSIVE";
            await command.ExecuteNonQueryAsync(token);

            (await locks.TryAcquireAsync(Name, TimeSpan.FromMilliseconds(500), token)).ShouldBeNull();
        }

        await using var held = await locks.TryAcquireAsync(Name, TimeSpan.Zero, token);
        held.ShouldNotBeNull();
    }
}
