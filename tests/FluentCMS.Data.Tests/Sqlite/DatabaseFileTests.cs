using FluentCMS.Core.Storage;
using FluentCMS.Data.Sqlite;
using FluentCMS.Data.Tests.Providers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Sqlite;

// The database file behind backup and restore: the online snapshot, the check for a host that has the file open, and the
// restore that leaves nothing of the replaced database's journal behind.
public sealed class DatabaseFileTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("fluentcms-database-file-tests-");

    private string Database => Path.Combine(_directory.FullName, "cms.db");

    private IDatabaseFile Slot => new SqliteDataProvider(Connection(Database));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _directory.Delete(recursive: true);
    }

    private static string Connection(string file) => new SqliteConnectionStringBuilder { DataSource = file, Pooling = false }.ToString();

    private static SqliteConnection Open(string file, string setup = "PRAGMA journal_mode = WAL")
    {
        var connection = new SqliteConnection(Connection(file));
        connection.Open();
        Execute(connection, setup);
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
#pragma warning disable CA2100 // The tests' own statements.
        command.CommandText = sql;
#pragma warning restore CA2100
        command.ExecuteNonQuery();
    }

    private static long Count(string file, string table)
    {
        using var connection = Open(file, "SELECT 1");
        using var command = connection.CreateCommand();
#pragma warning disable CA2100
        command.CommandText = $"SELECT count(*) FROM {table}";
#pragma warning restore CA2100
        return (long)command.ExecuteScalar()!;
    }

    [SqliteFact("SQLite's online backup")]
    public void TheFileIsTheFullPathOfTheDataSourceAndAnInMemoryDatabaseHasNone()
    {
        Slot.FilePath.ShouldBe(Database);
        ((IDatabaseFile)new SqliteDataProvider("Data Source=cms;Mode=Memory;Cache=Shared")).FilePath.ShouldBeNull();
        ((IDatabaseFile)new SqliteDataProvider("Data Source=:memory:")).FilePath.ShouldBeNull();
    }

    [SqliteFact("SQLite's locks")]
    public void TheDatabaseIsInUseWhileAnotherConnectionHasItOpen()
    {
        using (var writer = Open(Database))
        {
            Execute(writer, "CREATE TABLE note (id INTEGER); INSERT INTO note VALUES (1);");

            Slot.IsInUse().ShouldBeTrue();
        }

        Slot.IsInUse().ShouldBeFalse();
        Count(Database, "note").ShouldBe(1);
    }

    [SqliteFact("SQLite's locks")]
    public void ADatabaseThatDoesNotExistIsNotInUseAndIsNotCreated()
    {
        Slot.IsInUse().ShouldBeFalse();

        File.Exists(Database).ShouldBeFalse();
    }

    [SqliteFact("SQLite's online backup")]
    public void ASnapshotWhileAWriterHoldsATransactionHasWhatWasCommittedInOneFile()
    {
        var snapshot = Path.Combine(_directory.FullName, "snapshot.db");
        using var writer = Open(Database);
        Execute(writer, "CREATE TABLE note (id INTEGER); INSERT INTO note VALUES (1);");
        using var transaction = writer.BeginTransaction();
        Execute(writer, "INSERT INTO note VALUES (2)");

        Slot.Snapshot(snapshot);

        transaction.Rollback();
        Count(snapshot, "note").ShouldBe(1);
        File.Exists(snapshot + "-wal").ShouldBeFalse();
        using var copy = Open(snapshot, "SELECT 1");
        using var mode = copy.CreateCommand();
        mode.CommandText = "PRAGMA journal_mode";
        mode.ExecuteScalar().ShouldBe("delete");
    }

    [SqliteFact("SQLite's journal files")]
    public void ARestoreReplacesTheFileAndRemovesTheJournalOfTheDatabaseItReplaces()
    {
        var snapshot = Path.Combine(_directory.FullName, "snapshot.db");
        using (var source = Open(snapshot, "PRAGMA journal_mode = DELETE"))
        {
            Execute(source, "CREATE TABLE restored (id INTEGER); INSERT INTO restored VALUES (1);");
        }

        using (var old = Open(Database))
        {
            Execute(old, "CREATE TABLE replaced (id INTEGER);");
        }

        File.WriteAllText(Database + "-wal", "stale");
        File.WriteAllText(Database + "-shm", "stale");

        Slot.Restore(snapshot);

        File.Exists(snapshot).ShouldBeFalse();
        File.Exists(Database + "-wal").ShouldBeFalse();
        File.Exists(Database + "-shm").ShouldBeFalse();
        Count(Database, "restored").ShouldBe(1);
    }

    // `backup` leaves its copy in the rollback journal; a file put in an archive by hand may be in WAL mode.
    [SqliteTheory("SQLite's journal modes")]
    [InlineData("DELETE")]
    [InlineData("WAL")]
    public void AHostStartsOnARestoredFileWhateverTheJournalModeOfTheBackupAndLeavesItInWalMode(string journalMode)
    {
        var snapshot = Path.Combine(_directory.FullName, "snapshot.db");
        using (var source = Open(snapshot, $"PRAGMA journal_mode = {journalMode}"))
        {
            Execute(source, "CREATE TABLE restored (id INTEGER); INSERT INTO restored VALUES (1);");
        }

        var provider = new SqliteDataProvider(Connection(Database));
        ((IDatabaseFile)provider).Restore(snapshot);

        var options = new DbContextOptionsBuilder<SystemDbContext>();
        provider.ConfigureSystemMigrations(options);
        options.UseSystemModel(provider.Conventions);
        using (var context = new SystemDbContext(options.Options))
        {
            context.Database.Migrate();
        }

        using var restored = Open(Database, "SELECT 1");
        using var mode = restored.CreateCommand();
        mode.CommandText = "PRAGMA journal_mode";
        mode.ExecuteScalar().ShouldBe("wal");
        Count(Database, "restored").ShouldBe(1);
    }
}
