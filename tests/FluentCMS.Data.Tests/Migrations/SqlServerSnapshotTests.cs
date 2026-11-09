using FluentCMS.Core.Storage;
using FluentCMS.Data.SqlServer;
using FluentCMS.Data.Tests.Providers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace FluentCMS.Data.Tests.Migrations;

// ADR 0005. The run's databases have READ_COMMITTED_SNAPSHOT on from model, so each test forgets the migration that turns it
// on, and turns it off where it needs to. No connection is pooled: turning it on or off ends the other sessions of the database.
public sealed class SqlServerSnapshotTests : IDisposable
{
    private const string Because = "the system migration that turns READ_COMMITTED_SNAPSHOT on";
    private const string Migration = "20261109212001_EnableReadCommittedSnapshot";

    private readonly TestDatabase _database = TestDatabase.Create(forSystemTables: true);
    private string? _login;

    private string Admin => new SqlConnectionStringBuilder(_database.ConnectionString) { Pooling = false }.ToString();

    private string Name => new SqlConnectionStringBuilder(_database.ConnectionString).InitialCatalog;

    public void Dispose()
    {
        try
        {
            if (TestProvider.IsSqlServer)
            {
                if (_login is not null)
                {
                    Run(Admin, $"DROP USER IF EXISTS [{_login}]; DROP LOGIN [{_login}];");
                }

                if (!IsOn())
                {
                    SetSnapshot(true);
                }
            }
        }
        finally
        {
            _database.Dispose();
        }
    }

    [SqlServerFact(Because)]
    public void TheSystemMigrationsTurnItOnAndRollBackTheOtherSessionsOfTheDatabase()
    {
        Migrate(Admin);
        SetSnapshot(false);
        Forget();

        using var other = new SqlConnection(Admin);
        other.Open();
        // Not disposed: its session is ended under it.
        var transaction = other.BeginTransaction();
        using (var command = other.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO cms_lock (name) VALUES (N'held')";
            command.ExecuteNonQuery();
        }

        Migrate(Admin);

        IsOn().ShouldBeTrue();
        Applied().ShouldContain(Migration);
        Read("SELECT COUNT(*) FROM cms_lock WHERE name = N'held'").ShouldBe(0);
    }

    [SqlServerFact(Because)]
    public void WhereItIsOnAlreadyTheMigrationNeedsNoPermissionOnTheDatabase()
    {
        Migrate(Admin);
        Forget();

        Migrate(LimitedUser());

        IsOn().ShouldBeTrue();
        Applied().ShouldContain(Migration);
    }

    [SqlServerFact(Because)]
    public void WhereItIsOffAndTheUserMayNotTurnItOnTheMigrationFailsAndSaysWhatToRun()
    {
        Migrate(Admin);
        SetSnapshot(false);
        Forget();

        var exception = Should.Throw<SqlException>(() => Migrate(LimitedUser()));

        exception.Number.ShouldBe(50000);
        exception.Message.ShouldBe(
            $"FluentCMS needs READ_COMMITTED_SNAPSHOT on in its SQL Server database, and the user that applies the migrations has no ALTER permission on the database to turn it on. Run ALTER DATABASE [{Name}] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE as a user that has it, then apply the migrations again.");
        IsOn().ShouldBeFalse();
        Applied().ShouldNotContain(Migration);
    }

    private static void Migrate(string connectionString)
    {
        var provider = new SqlServerDataProvider(connectionString);
        var options = new DbContextOptionsBuilder<SystemDbContext>();
        provider.ConfigureSystemMigrations(options);
        options.UseSystemModel(provider.Conventions);
        using var context = new SystemDbContext(options.Options);
        context.Database.Migrate();
    }

    // A user that reads and writes the tables, as one that applies only migrations without DDL would, with no ALTER on the database.
    private string LimitedUser()
    {
        _login = $"l_{Guid.NewGuid():N}";
        var password = $"{Guid.NewGuid():N}aA1!";
        Run(Admin, $"""
            CREATE LOGIN [{_login}] WITH PASSWORD = N'{password}', CHECK_POLICY = OFF;
            CREATE USER [{_login}] FOR LOGIN [{_login}];
            ALTER ROLE db_datareader ADD MEMBER [{_login}];
            ALTER ROLE db_datawriter ADD MEMBER [{_login}];
            """);

        return new SqlConnectionStringBuilder(Admin) { UserID = _login, Password = password }.ToString();
    }

    private void SetSnapshot(bool on) =>
        Run(Admin, $"ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT {(on ? "ON" : "OFF")} WITH ROLLBACK IMMEDIATE");

    private void Forget() => Run(Admin, $"DELETE FROM {SystemDbContext.HistoryTable} WHERE MigrationId = N'{Migration}'");

    private bool IsOn() => Read("SELECT CAST(is_read_committed_snapshot_on AS int) FROM sys.databases WHERE database_id = DB_ID()") == 1;

    private List<string> Applied()
    {
        using var connection = new SqlConnection(Admin);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT MigrationId FROM {SystemDbContext.HistoryTable}";
        using var reader = command.ExecuteReader();

        var ids = new List<string>();
        while (reader.Read())
        {
            ids.Add(reader.GetString(0));
        }

        return ids;
    }

    private int Read(string sql)
    {
        using var connection = new SqlConnection(Admin);
        connection.Open();
        using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Constants of the tests.
        command.CommandText = sql;
#pragma warning restore CA2100
        return (int)command.ExecuteScalar()!;
    }

    private static void Run(string connectionString, string sql)
    {
        using var connection = new SqlConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Names the test makes up.
        command.CommandText = sql;
#pragma warning restore CA2100
        command.ExecuteNonQuery();
    }
}
