using FluentCMS.Core.Storage;
using FluentCMS.Data.Sqlite;
using FluentCMS.Data.Tests.Providers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Sqlite;

// What a connection the provider opens is set to. The provider is used as a host gets it, without the tests' unflushed commits.
public sealed class JournalModeTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("fluentcms-journal-tests-");

    private string File => Path.Combine(_directory.FullName, "cms.db");

    private string FileConnectionString => new SqliteConnectionStringBuilder { DataSource = File, Pooling = false }.ToString();

    public void Dispose() => _directory.Delete(recursive: true);

    private static SystemDbContext Context(SqliteDataProvider provider)
    {
        var options = new DbContextOptionsBuilder<SystemDbContext>();
        provider.ConfigureSystemMigrations(options);
        options.UseSystemModel(provider.Conventions);
        return new SystemDbContext(options.Options);
    }

    private static object? Pragma(SystemDbContext context, string name)
    {
        using var command = context.Database.GetDbConnection().CreateCommand();
#pragma warning disable CA2100 // Pragma names of the tests.
        command.CommandText = $"PRAGMA {name}";
#pragma warning restore CA2100
        return command.ExecuteScalar();
    }

    // A file another tool made, which keeps the rollback journal until something changes it.
    private void CreateInRollbackJournal()
    {
        using var connection = new SqliteConnection(FileConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE made_elsewhere (id INTEGER); PRAGMA journal_mode = delete;";
        command.ExecuteNonQuery();
    }

    [SqliteFact("SQLite's journal modes")]
    public void AFileDatabaseIsOpenedInWalModeWithABusyTimeout()
    {
        CreateInRollbackJournal();
        using var context = Context(new SqliteDataProvider(FileConnectionString));

        context.Database.OpenConnection();

        Pragma(context, "journal_mode").ShouldBe("wal");
        Pragma(context, "synchronous").ShouldBe(1L);
        Pragma(context, "busy_timeout").ShouldBe(5000L);
    }

    [SqliteFact("SQLite's journal modes")]
    public void EveryConnectionGetsItsOwnSettings()
    {
        var provider = new SqliteDataProvider(FileConnectionString);
        using (var first = Context(provider))
        {
            first.Database.OpenConnection();
        }

        using var second = Context(provider);
        second.Database.OpenConnection();

        Pragma(second, "journal_mode").ShouldBe("wal");
        Pragma(second, "synchronous").ShouldBe(1L);
        Pragma(second, "busy_timeout").ShouldBe(5000L);
    }

    [SqliteFact("SQLite's journal modes")]
    public void AHostThatOptsOutKeepsTheJournalModeAndSqliteDefaults()
    {
        CreateInRollbackJournal();
        using var context = Context(new SqliteDataProvider(FileConnectionString, writeAheadLog: false));

        context.Database.OpenConnection();

        Pragma(context, "journal_mode").ShouldBe("delete");
        Pragma(context, "synchronous").ShouldBe(2L);
        Pragma(context, "busy_timeout").ShouldBe(0L);
    }

    [SqliteTheory("SQLite's journal modes")]
    [InlineData("Data Source=:memory:")]
    [InlineData("Data Source=journal-tests;Mode=Memory;Cache=Shared")]
    public void AnInMemoryDatabaseIsLeftAsItIs(string connectionString)
    {
        using var context = Context(new SqliteDataProvider(connectionString));

        context.Database.OpenConnection();

        Pragma(context, "journal_mode").ShouldBe("memory");
        Pragma(context, "busy_timeout").ShouldBe(0L);
    }
}
