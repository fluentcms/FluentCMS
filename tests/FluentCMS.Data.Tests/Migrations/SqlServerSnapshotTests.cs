using FluentCMS.Core.Storage;
using FluentCMS.Data.SqlServer;
using FluentCMS.Data.Tests.Providers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace FluentCMS.Data.Tests.Migrations;

// ADR 0005. The run's databases have READ_COMMITTED_SNAPSHOT on from model, so each test takes an empty database and turns it
// off where it needs to. The baseline turns it on before it creates anything. No connection is pooled: turning it on or off
// ends the other sessions of the database.
public sealed class SqlServerSnapshotTests : IDisposable
{
    private const string Because = "the system baseline, which turns READ_COMMITTED_SNAPSHOT on";

    private readonly TestDatabase _database = TestDatabase.Create();
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
    public void TheBaselineTurnsItOnAndRollsBackTheOtherSessionsOfTheDatabase()
    {
        SetSnapshot(false);
        Run(Admin, "CREATE TABLE held (name nvarchar(64) NOT NULL)");

        using var other = new SqlConnection(Admin);
        other.Open();
        // Not disposed: its session is ended under it.
        var transaction = other.BeginTransaction();
        using (var command = other.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO held (name) VALUES (N'held')";
            command.ExecuteNonQuery();
        }

        Migrate(Admin);

        IsOn().ShouldBeTrue();
        Applied().ShouldBe([Baseline()]);
        Read("SELECT COUNT(*) FROM held").ShouldBe(0);
    }

    [SqlServerFact(Because)]
    public void WhereItIsOnAlreadyTheBaselineNeedsNoAlterOnTheDatabase()
    {
        Migrate(LimitedUser());

        IsOn().ShouldBeTrue();
        Applied().ShouldBe([Baseline()]);
    }

    [SqlServerFact(Because)]
    public void WhereItIsOffAndTheUserMayNotTurnItOnTheBaselineFailsSaysWhatToRunAndCreatesNothing()
    {
        SetSnapshot(false);

        var exception = Should.Throw<SqlException>(() => Migrate(LimitedUser()));

        exception.Number.ShouldBe(50000);
        exception.Message.ShouldBe(
            $"FluentCMS needs READ_COMMITTED_SNAPSHOT on in its SQL Server database, and the user that applies the migrations has no ALTER permission on the database to turn it on. Run ALTER DATABASE [{Name}] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE as a user that has it, then apply the migrations again.");
        IsOn().ShouldBeFalse();
        Applied().ShouldBeEmpty();

        // EF creates its history table before it applies anything.
        Strings("SELECT name FROM sys.tables").ShouldBe([SystemDbContext.HistoryTable]);
    }

    private static string Baseline()
    {
        using var context = Context("Server=localhost;Database=never_opened");
        return context.Database.GetMigrations().ShouldHaveSingleItem();
    }

    private static void Migrate(string connectionString)
    {
        using var context = Context(connectionString);
        context.Database.Migrate();
    }

    private static SystemDbContext Context(string connectionString)
    {
        var provider = new SqlServerDataProvider(connectionString);
        var options = new DbContextOptionsBuilder<SystemDbContext>();
        provider.ConfigureSystemMigrations(options);
        options.UseSystemModel(provider.Conventions);
        return new SystemDbContext(options.Options);
    }

    // A user that may create the tables and write their rows, with no ALTER on the database itself.
    private string LimitedUser()
    {
        _login = $"l_{Guid.NewGuid():N}";
        var password = $"{Guid.NewGuid():N}aA1!";
        Run(Admin, $"""
            CREATE LOGIN [{_login}] WITH PASSWORD = N'{password}', CHECK_POLICY = OFF;
            CREATE USER [{_login}] FOR LOGIN [{_login}];
            ALTER ROLE db_datareader ADD MEMBER [{_login}];
            ALTER ROLE db_datawriter ADD MEMBER [{_login}];
            GRANT CREATE TABLE TO [{_login}];
            GRANT ALTER, REFERENCES ON SCHEMA::dbo TO [{_login}];
            """);

        return new SqlConnectionStringBuilder(Admin) { UserID = _login, Password = password }.ToString();
    }

    private void SetSnapshot(bool on) =>
        Run(Admin, $"ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT {(on ? "ON" : "OFF")} WITH ROLLBACK IMMEDIATE");

    private bool IsOn() => Read("SELECT CAST(is_read_committed_snapshot_on AS int) FROM sys.databases WHERE database_id = DB_ID()") == 1;

    private List<string> Applied() =>
        Strings($"IF OBJECT_ID(N'{SystemDbContext.HistoryTable}') IS NOT NULL SELECT MigrationId FROM {SystemDbContext.HistoryTable}");

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

    private List<string> Strings(string sql)
    {
        using var connection = new SqlConnection(Admin);
        connection.Open();
        using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Constants of the tests.
        command.CommandText = sql;
#pragma warning restore CA2100
        using var reader = command.ExecuteReader();

        var values = new List<string>();
        while (reader.Read())
        {
            values.Add(reader.GetString(0));
        }

        return values;
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
