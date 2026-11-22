using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using FluentCMS.Data.Tests.Providers;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace FluentCMS.Data.Tests.Migrations;

// What the server says of the migrations lock of a database. Read without pooling: a connection of the pool would reuse the
// session under test, and the reset of that reuse releases the lock a test looks for.
internal static partial class MigrationLockSessions
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(50);

    // The query that docs/content-migrations.md gives an operator.
    public static string HoldersQuery { get; } = Documented();

    private static string WaitersQuery => TestProvider.IsPostgreSql
        ? "SELECT pid FROM pg_locks WHERE locktype = 'advisory' AND NOT granted AND database = (SELECT oid FROM pg_database WHERE datname = current_database())"
        : "SELECT request_session_id FROM sys.dm_tran_locks WHERE resource_type = 'APPLICATION' AND request_status <> 'GRANT' AND resource_database_id = DB_ID()";

    public static DbConnection Connect(TestDatabase database) => TestProvider.IsPostgreSql
        ? new NpgsqlConnection(new NpgsqlConnectionStringBuilder(database.ConnectionString) { Pooling = false }.ToString())
        : new SqlConnection(new SqlConnectionStringBuilder(database.ConnectionString) { Pooling = false }.ToString());

    public static Task<List<string>> HoldersAsync(TestDatabase database, CancellationToken cancellationToken) =>
        ReadAsync(database, HoldersQuery, cancellationToken);

    public static Task<List<string>> WaitersAsync(TestDatabase database, CancellationToken cancellationToken) =>
        ReadAsync(database, WaitersQuery, cancellationToken);

    public static Task UntilHeldAsync(TestDatabase database, Task holder, CancellationToken cancellationToken) =>
        UntilAsync(async () => (await HoldersAsync(database, cancellationToken)).Count > 0, holder, "the lock to be held", cancellationToken);

    public static Task UntilWaitedForAsync(TestDatabase database, Task waiter, CancellationToken cancellationToken) =>
        UntilAsync(async () => (await WaitersAsync(database, cancellationToken)).Count > 0, waiter, "a wait for the lock", cancellationToken);

    // A session that was ended releases its locks as the server notices, so this looks until it has.
    public static Task UntilFreeAsync(TestDatabase database, CancellationToken cancellationToken) =>
        UntilAsync(async () => (await HoldersAsync(database, cancellationToken)).Count == 0, null, "the lock to be free", cancellationToken);

    private static async Task UntilAsync(Func<Task<bool>> seen, Task? running, string what, CancellationToken cancellationToken)
    {
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bound.CancelAfter(Limit);
        while (!await seen())
        {
            if (running is { IsCompleted: true })
            {
                await running;
                throw new InvalidOperationException($"The task ended before the server showed {what}.");
            }

            try
            {
                await Task.Delay(Poll, bound.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Waited {Limit.TotalSeconds:0} seconds for {what}.");
            }
        }
    }

    private static async Task<List<string>> ReadAsync(TestDatabase database, string sql, CancellationToken cancellationToken)
    {
        await using var connection = Connect(database);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Queries of the tests and of the repository's documentation.
        command.CommandText = sql;
#pragma warning restore CA2100
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var sessions = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            sessions.Add(Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture)!);
        }

        return sessions;
    }

    private static string Documented()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "FluentCMS.slnx")))
        {
            directory = directory.Parent ?? throw new InvalidOperationException($"FluentCMS.slnx was not found above {AppContext.BaseDirectory}.");
        }

        var guide = File.ReadAllText(Path.Combine(directory.FullName, "docs", "content-migrations.md")).ReplaceLineEndings("\n");
        var view = TestProvider.IsPostgreSql ? "FROM pg_locks" : "FROM sys.dm_tran_locks";
        return SqlBlock().Matches(guide).Select(match => match.Groups[1].Value).Single(sql => sql.Contains(view, StringComparison.Ordinal));
    }

    [GeneratedRegex("^```sql\n(.*?)^```$", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex SqlBlock();
}
