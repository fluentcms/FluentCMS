using System.Runtime.CompilerServices;
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

    // The run's provider for work that never opens a connection, such as scaffolding a migration.
    public static IDataProvider Offline() => Name switch
    {
        Sqlite => new SqliteDataProvider("Data Source=:memory:"),
        PostgreSql => new PostgreSqlDataProvider("Host=localhost;Database=never_opened"),
        _ => new SqlServerDataProvider("Server=localhost;Database=never_opened", nativeJson: Name == SqlServer2025),
    };

    public static string SqliteOnlyReason(string? because) =>
        $"Runs on {Sqlite} only{(because is null ? "" : $", because it tests {because}")}; this run is {Name}.";

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
