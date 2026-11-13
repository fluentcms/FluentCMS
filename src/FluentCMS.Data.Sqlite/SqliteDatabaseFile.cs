using FluentCMS.Core.Storage;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace FluentCMS.Data.Sqlite;

// The database file behind `fluentcms backup` and `fluentcms restore`. The snapshot goes through SQLite's online backup,
// which copies the pages under a read transaction, so it is consistent while the host writes, in WAL mode without stopping
// it. A restore replaces the file and removes the journal files of the database it replaces, whose pages would otherwise be
// read into the new one. The file keeps the journal mode of the backup until the provider opens it to write, which puts it
// in WAL mode when the provider uses it.
internal sealed class SqliteDatabaseFile(string connectionString) : IDatabaseFile
{
    private const string BusyTimeout = "PRAGMA busy_timeout = 5000";
    private const int SnapshotAttempts = 10;

    private static readonly string[] JournalSuffixes = ["-wal", "-shm", "-journal"];

    private readonly Lazy<string?> _path = new(() => PathOf(connectionString));

    // Read when a command asks, so that the provider parses its connection string no earlier than it did.
    public string? FilePath => _path.Value;

    // A reader of a database in WAL mode is turned away for a moment while a writer of the running host checkpoints, so the
    // busy timeout waits for it, and a copy that still finds the source busy is taken again.
    public void Snapshot(string destination)
    {
        using var source = new SqliteConnection(connectionString);
        source.Open();
        Execute(source, BusyTimeout);
        using var target = new SqliteConnection(Unpooled(destination, SqliteOpenMode.ReadWriteCreate));
        target.Open();
        Execute(target, BusyTimeout);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                source.BackupDatabase(target);
                break;
            }
            catch (SqliteException exception) when (exception.SqliteErrorCode is raw.SQLITE_BUSY or raw.SQLITE_LOCKED && attempt < SnapshotAttempts)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(100 * attempt));
            }
        }

        // The copy has the journal mode of the source; a file in the rollback journal is whole once it is closed.
        Execute(target, "PRAGMA journal_mode = DELETE");
    }

    // A connection in WAL mode holds a shared lock on the file from its first read until it closes, so the exclusive lock of
    // the exclusive locking mode is refused while any other connection, in this process or another, has the database open.
    // A host that keeps the rollback journal holds no lock while it is idle, so it is not seen; the guide says to stop the
    // host first. Each statement is a command of its own: Microsoft.Data.Sqlite stops at the row the pragma answers.
    public bool IsInUse()
    {
        if (FilePath is null || !File.Exists(FilePath))
        {
            return false;
        }

        SqliteConnection.ClearAllPools();
        using var connection = new SqliteConnection(Unpooled(FilePath, SqliteOpenMode.ReadWrite));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 1;
        command.CommandText = "PRAGMA locking_mode = EXCLUSIVE";
        command.ExecuteNonQuery();
        command.CommandText = "SELECT count(*) FROM sqlite_master";
        try
        {
            command.ExecuteScalar();
            return false;
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode is raw.SQLITE_BUSY or raw.SQLITE_LOCKED)
        {
            return true;
        }
    }

    public void Restore(string snapshot)
    {
        var path = FilePath ?? throw new InvalidOperationException("An in-memory database has no file to restore.");

        SqliteConnection.ClearAllPools();

        // The database it replaces is moved aside first and back when a later step fails, so a failed restore loses nothing.
        var aside = File.Exists(path) ? $"{path}.{Guid.NewGuid():N}.replaced" : null;
        if (aside is not null)
        {
            File.Move(path, aside);
        }

        try
        {
            foreach (var suffix in JournalSuffixes)
            {
                File.Delete(path + suffix);
            }

            File.Move(snapshot, path);
        }
        catch when (aside is not null && !File.Exists(path))
        {
            File.Move(aside, path);
            throw;
        }

        if (aside is not null)
        {
            File.Delete(aside);
        }
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    // Microsoft.Data.Sqlite opens a relative path against the current directory, which is the project directory of a host
    // the tool runs.
    private static string? PathOf(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        var source = builder.DataSource;
        return builder.Mode == SqliteOpenMode.Memory || string.IsNullOrEmpty(source) || source == ":memory:"
            || source.StartsWith("file:", StringComparison.OrdinalIgnoreCase) || source.Contains("|DataDirectory|", StringComparison.OrdinalIgnoreCase)
            ? null
            : Path.GetFullPath(source);
    }

    private static string Unpooled(string path, SqliteOpenMode mode) =>
        new SqliteConnectionStringBuilder { DataSource = path, Mode = mode, Pooling = false }.ToString();
}
