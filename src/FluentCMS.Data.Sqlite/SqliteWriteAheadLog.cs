using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SQLitePCL;

namespace FluentCMS.Data.Sqlite;

// In WAL mode readers and the writer do not block each other, where the rollback journal makes every commit wait for a
// moment with no reader. The mode belongs to the file and outlives the connection, so it is asked for until SQLite reports
// it once. The busy timeout and synchronous belong to each connection. With the timeout a writer waits for the write lock
// inside SQLite instead of in the 150 ms retries of Microsoft.Data.Sqlite, which give up the lock between tries.
internal sealed class SqliteWriteAheadLog : DbConnectionInterceptor
{
    private const string BusyTimeout = "PRAGMA busy_timeout = 5000";

    private volatile bool _wal;

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) => Configure(connection);

    // Microsoft.Data.Sqlite runs its asynchronous calls synchronously, so there is nothing to await.
    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Configure(connection);
        return Task.CompletedTask;
    }

    private void Configure(DbConnection connection)
    {
        // SQLite names no file for an in-memory or temporary database, whatever the connection string says.
        if (string.IsNullOrEmpty(connection.DataSource))
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.CommandText = BusyTimeout;
        command.ExecuteNonQuery();

        // Leaving the rollback journal writes to the file, which a read-only connection is refused, and EF opens one to see
        // whether the database exists before anything else. So the first connection that may write switches the file.
        if (!_wal && !IsReadOnly(connection))
        {
            command.CommandText = "PRAGMA journal_mode = WAL";
            _wal = command.ExecuteScalar() is "wal";
        }

        // NORMAL loses at most the last commits on a power cut in WAL mode, but can corrupt a rollback journal database,
        // so a file that stayed in that mode (a share without shared memory) keeps FULL.
        if (_wal)
        {
            command.CommandText = "PRAGMA synchronous = NORMAL";
            command.ExecuteNonQuery();
        }
    }

    // SQLite also opens a file read-only that the connection string lets write when the file system does not.
    private static bool IsReadOnly(DbConnection connection) =>
        connection is SqliteConnection { Handle: { } handle } && raw.sqlite3_db_readonly(handle, "main") == 1;
}
