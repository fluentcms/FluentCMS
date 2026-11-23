using FluentCMS.Core.Storage;
using FluentCMS.Data.PostgreSql;
using FluentCMS.Data.Sqlite;
using FluentCMS.Data.SqlServer;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Providers.UniqueViolationTests;

namespace FluentCMS.Data.Tests.Providers;

// Only the refusal of a foreign key lets the removal of an MCP client go on to the next one (#3448). SQLite raises its
// errors for real; the PostgreSQL and SQL Server ones are built as their drivers build them, with the messages of a server
// in English, since no server runs here.
public class ForeignKeyViolationTests
{
    private static readonly SqliteDataProvider Sqlite = new("Data Source=:memory:");
    private static readonly PostgreSqlDataProvider PostgreSql = new("Host=localhost;Database=never_opened");
    private static readonly SqlServerDataProvider SqlServer = new("Server=localhost;Database=never_opened");

    private const string Tables = "pragma foreign_keys = on; create table parent (id integer primary key); create table child (id integer primary key, parent_id integer not null references parent (id)); insert into parent values (1); insert into child values (1, 1)";

    private static SqliteException SqliteError(string setup, string failing)
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        Run(connection, setup);
        return Should.Throw<SqliteException>(() => Run(connection, failing));
    }

    private static void Run(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Constants of the tests.
        command.CommandText = sql;
#pragma warning restore CA2100
        command.ExecuteNonQuery();
    }

    private static DbUpdateException Saved(Exception inner) => new("An error occurred while saving the entity changes.", inner);

    [Theory]
    [InlineData("delete from parent where id = 1")]
    [InlineData("insert into child values (2, 7)")]
    public void SqliteKnowsAForeignKey(string failing)
    {
        var error = SqliteError(Tables, failing);

        Sqlite.IsForeignKeyViolation(error).ShouldBeTrue();
        Sqlite.IsForeignKeyViolation(Saved(error)).ShouldBeTrue();
    }

    [Theory]
    [InlineData("create table t (x text check (x <> 'a'))", "insert into t values ('a')")]
    [InlineData("create table t (x text unique); insert into t values ('a')", "insert into t values ('a')")]
    [InlineData("create table t (x text not null)", "insert into t values (null)")]
    public void SqliteTellsAnotherConstraintApart(string setup, string failing)
    {
        var error = SqliteError(setup, failing);

        Sqlite.IsForeignKeyViolation(error).ShouldBeFalse();
        Sqlite.IsForeignKeyViolation(Saved(error)).ShouldBeFalse();
    }

    // A prediction of the server's answer: SqlState 23503, as Npgsql names it.
    [Fact]
    public void PostgreSqlKnowsSqlState23503()
    {
        PostgreSql.IsForeignKeyViolation(PostgreSqlError(PostgresErrorCodes.ForeignKeyViolation)).ShouldBeTrue();
        PostgreSql.IsForeignKeyViolation(Saved(PostgreSqlError("23503"))).ShouldBeTrue();
    }

    [Theory]
    [InlineData(PostgresErrorCodes.UniqueViolation)]
    [InlineData(PostgresErrorCodes.CheckViolation)]
    [InlineData(PostgresErrorCodes.NotNullViolation)]
    public void PostgreSqlTellsAnotherErrorApart(string sqlState) =>
        PostgreSql.IsForeignKeyViolation(Saved(PostgreSqlError(sqlState))).ShouldBeFalse();

    // Predictions of the server's messages: 547 is every conflict with a constraint, and the constraint it quotes, or the
    // kind its text names, tells a foreign key, in a message of another language too.
    [Theory]
    [InlineData("The DELETE statement conflicted with the REFERENCE constraint \"FK_cms_oidc_authorization_cms_oidc_application_application_id\". The conflict occurred in database \"cms\", table \"dbo.cms_oidc_authorization\", column 'application_id'.")]
    [InlineData("The INSERT statement conflicted with the FOREIGN KEY constraint \"FK_cms_oidc_token_cms_oidc_application_application_id\". The conflict occurred in database \"cms\", table \"dbo.cms_oidc_application\", column 'id'.")]
    [InlineData("Die DELETE-Anweisung steht in Konflikt mit der Einschränkung \"FK_cms_oidc_authorization_cms_oidc_application_application_id\".")]
    public void SqlServerKnowsAForeignKeyOf547(string message)
    {
        SqlServer.IsForeignKeyViolation(SqlServerError(547, message)).ShouldBeTrue();
        SqlServer.IsForeignKeyViolation(Saved(SqlServerError(547, message))).ShouldBeTrue();
    }

    [Theory]
    [InlineData(547, "The INSERT statement conflicted with the CHECK constraint \"ck_cms_outbox_payload_json\". The conflict occurred in database \"cms\", table \"dbo.cms_outbox\", column 'payload'.")]
    [InlineData(2627, "Violation of PRIMARY KEY constraint \"PK_cms_lock\". Cannot insert duplicate key in object \"dbo.cms_lock\".")]
    [InlineData(1205, "Transaction was deadlocked on lock resources with another process and has been chosen as the deadlock victim.")]
    public void SqlServerTellsAnotherErrorApart(int number, string message) =>
        SqlServer.IsForeignKeyViolation(Saved(SqlServerError(number, message))).ShouldBeFalse();

    [Fact]
    public void NoProviderTakesAnotherProvidersErrorOrAPlainFailure()
    {
        var sqlite = SqliteError(Tables, "delete from parent where id = 1");
        var postgreSql = PostgreSqlError(PostgresErrorCodes.ForeignKeyViolation);
        var sqlServer = SqlServerError(547, "The DELETE statement conflicted with the REFERENCE constraint \"FK_x\".");

        foreach (var (provider, foreign) in new (IDataProvider, Exception[])[] { (Sqlite, [postgreSql, sqlServer]), (PostgreSql, [sqlite, sqlServer]), (SqlServer, [sqlite, postgreSql]) })
        {
            foreach (var exception in foreign.Concat([new InvalidOperationException("Not a database error."), new DbUpdateException("No inner error.")]))
            {
                provider.IsForeignKeyViolation(exception).ShouldBeFalse($"{provider.Name}: {exception.GetType().Name}");
                provider.IsForeignKeyViolation(Saved(exception)).ShouldBeFalse($"{provider.Name}: saved {exception.GetType().Name}");
            }
        }
    }
}
