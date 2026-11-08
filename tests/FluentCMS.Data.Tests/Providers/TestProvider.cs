using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FluentCMS.Core.Storage;
using FluentCMS.Data.PostgreSql;
using FluentCMS.Data.SqlServer;
using FluentCMS.Data.Sqlite;
using Xunit;

namespace FluentCMS.Data.Tests.Providers;

// The database the data tests run on, from FLUENTCMS_TEST_PROVIDER. Unset is SQLite.
public static class TestProvider
{
    public const string Variable = "FLUENTCMS_TEST_PROVIDER";
    public const string Sqlite = "sqlite";
    public const string PostgreSql = "postgresql";
    public const string SqlServer2019 = "sqlserver-2019";
    public const string SqlServer2025 = "sqlserver-2025";

    public static string Name { get; } = Read();

    public static bool IsSqlite => Name == Sqlite;

    public static bool IsPostgreSql => Name == PostgreSql;

    public static bool IsSqlServer => Name is SqlServer2019 or SqlServer2025;

    // SQLite's two journal modes, for the tests of concurrent writers: EF creates a file in WAL mode, and one made
    // otherwise keeps the rollback journal. A server has neither, so such a test runs once.
    public static TheoryData<string> JournalModes => IsSqlite ? new() { "delete", "wal" } : new() { Name };

    // The driver's exception type, which a logged failure names.
    public static string DriverException => Name switch
    {
        Sqlite => "SqliteException",
        PostgreSql => "PostgresException",
        _ => "SqlException",
    };

    // How ToQueryString lists a parameter above the statement.
    public static string ParameterLinePrefix => Name switch
    {
        Sqlite => ".param",
        PostgreSql => "-- @",
        _ => "DECLARE @",
    };

    // An identifier as the run's provider writes it in the SQL that EF builds, for a test that recognises a command.
    // PostgreSQL quotes only the names that need it, and these tests name none of those.
    public static string Quote(string identifier) => Name switch
    {
        Sqlite => $"\"{identifier}\"",
        PostgreSql => identifier,
        _ => $"[{identifier}]",
    };

    // Whether a command EF built deletes rows of the table. SQL Server names the alias first: DELETE FROM [c] FROM [table] AS [c].
    public static bool IsDeleteFrom(string sql, string table) =>
        Regex.IsMatch(sql, $@"^DELETE FROM {(IsSqlServer ? @"\[\w+\]\s+FROM " : "")}{Regex.Escape(Quote(table))}(\s|$)");

    // The same for an ExecuteUpdate: UPDATE [c] SET ... FROM [table] AS [c] on SQL Server.
    public static bool IsUpdateOf(string sql, string table) =>
        Regex.IsMatch(sql, IsSqlServer ? $@"^UPDATE \[\w+\]\s+SET\s.*?\sFROM {Regex.Escape(Quote(table))}\s" : $@"^UPDATE {Regex.Escape(Quote(table))}(\s|$)", RegexOptions.Singleline);

    // Whether a command that SaveChanges built inserts into the table. SQL Server sends SET NOCOUNT ON before it.
    public static bool IsInsertInto(string sql, string table) =>
        Regex.IsMatch(sql, $@"(?m)^INSERT INTO {Regex.Escape(Quote(table))}(\s|$)");

    // The run's provider for work that never opens a connection, such as scaffolding a migration.
    public static IDataProvider Offline() => Name switch
    {
        Sqlite => new SqliteDataProvider("Data Source=:memory:"),
        PostgreSql => new PostgreSqlDataProvider("Host=localhost;Database=never_opened"),
        _ => new SqlServerDataProvider("Server=localhost;Database=never_opened"),
    };

    public static string SqliteOnlyReason(string? because) =>
        $"Runs on {Sqlite} only{(because is null ? "" : $", because it tests {because}")}; this run is {Name}.";

    public static string SqlServerOnlyReason(string because) =>
        $"Runs on SQL Server only, because it tests {because}; this run is {Name}.";

    private static string Read()
    {
        var name = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(name))
        {
            return Sqlite;
        }

        name = name.Trim().ToLowerInvariant();
        return name is Sqlite or PostgreSql or SqlServer2019 or SqlServer2025
            ? name
            : throw new InvalidOperationException($"{Variable} is '{name}'; use {Sqlite}, {PostgreSql}, {SqlServer2019} or {SqlServer2025}.");
    }
}

// A test of SQLite itself (its journal, locks, conventions or plans), skipped with the reason on the other providers.
public sealed class SqliteFactAttribute : FactAttribute
{
    public SqliteFactAttribute(string? because = null, [CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!TestProvider.IsSqlite)
        {
            Skip = TestProvider.SqliteOnlyReason(because);
        }
    }
}

public sealed class SqliteTheoryAttribute : TheoryAttribute
{
    public SqliteTheoryAttribute(string? because = null, [CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!TestProvider.IsSqlite)
        {
            Skip = TestProvider.SqliteOnlyReason(because);
        }
    }
}

// A test of SQL Server itself, skipped with the reason on the other providers.
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute(string because, [CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!TestProvider.IsSqlServer)
        {
            Skip = TestProvider.SqlServerOnlyReason(because);
        }
    }
}
