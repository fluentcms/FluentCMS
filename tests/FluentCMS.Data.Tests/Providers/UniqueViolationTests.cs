using System.Reflection;
using FluentCMS.Core.Storage;
using FluentCMS.Data.PostgreSql;
using FluentCMS.Data.Sqlite;
using FluentCMS.Data.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Providers;

// SQLite raises its errors for real; the PostgreSQL and SQL Server ones are built as their drivers build them, since no
// server runs before #2711.
public class UniqueViolationTests
{
    private static readonly SqliteDataProvider Sqlite = new("Data Source=:memory:");
    private static readonly PostgreSqlDataProvider PostgreSql = new("Host=localhost;Database=never_opened");
    private static readonly SqlServerDataProvider SqlServer = new("Server=localhost;Database=never_opened");

    private static SqliteException SqliteError(string table, string insert)
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        Run(connection, table);
        Run(connection, insert);
        return Should.Throw<SqliteException>(() => Run(connection, insert));
    }

    private static void Run(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Constants of the tests.
        command.CommandText = sql;
#pragma warning restore CA2100
        command.ExecuteNonQuery();
    }

    private static PostgresException PostgreSqlError(string sqlState) => new("duplicate key value", "ERROR", "ERROR", sqlState);

    // SqlClient has no public way to make one, so this takes the internal path the driver itself uses.
    private static SqlException SqlServerError(int number)
    {
        var constructor = typeof(SqlError).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).MaxBy(candidate => candidate.GetParameters().Length)!;
        var arguments = constructor.GetParameters()
            .Select(parameter => parameter.ParameterType == typeof(string) ? "" : parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null)
            .ToArray();
        arguments[0] = number;
        var error = (SqlError)constructor.Invoke(arguments);

        var errors = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;
        typeof(SqlErrorCollection).GetMethod("Add", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(errors, [error]);

        var create = typeof(SqlException).GetMethod("CreateException", BindingFlags.Static | BindingFlags.NonPublic, [typeof(SqlErrorCollection), typeof(string)])!;
        var exception = (SqlException)create.Invoke(null, [errors, "16.00.0000"])!;
        exception.Number.ShouldBe(number);
        return exception;
    }

    private static DbUpdateException Saved(Exception inner) => new("An error occurred while saving the entity changes.", inner);

    [Theory]
    [InlineData("create table t (x text unique)", "insert into t values ('a')")]
    [InlineData("create table t (x text, y text); create unique index ux_t on t (x, y) where y <> ''", "insert into t values ('a', 'b')")]
    [InlineData("create table t (x text primary key)", "insert into t values ('a')")]
    public void SqliteKnowsAUniqueOrPrimaryKey(string table, string insert)
    {
        var error = SqliteError(table, insert);

        Sqlite.IsUniqueViolation(error).ShouldBeTrue();
        Sqlite.IsUniqueViolation(Saved(error)).ShouldBeTrue();
    }

    [Theory]
    [InlineData("create table t (x text not null)", "insert into t values (null)")]
    [InlineData("create table t (x text check (x <> 'a'))", "insert into t values ('a')")]
    public void SqliteTellsAnotherConstraintApart(string table, string insert)
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        Run(connection, table);
        var error = Should.Throw<SqliteException>(() => Run(connection, insert));

        Sqlite.IsUniqueViolation(error).ShouldBeFalse();
        Sqlite.IsUniqueViolation(Saved(error)).ShouldBeFalse();
    }

    [Fact]
    public void PostgreSqlKnowsSqlState23505()
    {
        PostgreSql.IsUniqueViolation(PostgreSqlError(PostgresErrorCodes.UniqueViolation)).ShouldBeTrue();
        PostgreSql.IsUniqueViolation(Saved(PostgreSqlError("23505"))).ShouldBeTrue();
    }

    [Theory]
    [InlineData(PostgresErrorCodes.ForeignKeyViolation)]
    [InlineData(PostgresErrorCodes.NotNullViolation)]
    [InlineData(PostgresErrorCodes.SerializationFailure)]
    public void PostgreSqlTellsAnotherErrorApart(string sqlState) =>
        PostgreSql.IsUniqueViolation(Saved(PostgreSqlError(sqlState))).ShouldBeFalse();

    [Theory]
    [InlineData(2601)]
    [InlineData(2627)]
    public void SqlServerKnowsAUniqueIndexAndAUniqueConstraint(int number)
    {
        SqlServer.IsUniqueViolation(SqlServerError(number)).ShouldBeTrue();
        SqlServer.IsUniqueViolation(Saved(SqlServerError(number))).ShouldBeTrue();
    }

    [Theory]
    [InlineData(547)]
    [InlineData(515)]
    [InlineData(1205)]
    public void SqlServerTellsAnotherErrorApart(int number) =>
        SqlServer.IsUniqueViolation(Saved(SqlServerError(number))).ShouldBeFalse();

    [Fact]
    public void NoProviderTakesAnotherProvidersErrorOrAPlainFailure()
    {
        var sqlite = SqliteError("create table t (x text unique)", "insert into t values ('a')");
        var postgreSql = PostgreSqlError(PostgresErrorCodes.UniqueViolation);
        var sqlServer = SqlServerError(2601);

        foreach (var (provider, foreign) in new (IDataProvider, Exception[])[] { (Sqlite, [postgreSql, sqlServer]), (PostgreSql, [sqlite, sqlServer]), (SqlServer, [sqlite, postgreSql]) })
        {
            foreach (var exception in foreign.Concat([new InvalidOperationException("Not a database error."), Saved(new InvalidOperationException("Inner."))]))
            {
                provider.IsUniqueViolation(exception).ShouldBeFalse($"{provider.Name}: {exception.GetType().Name}");
                provider.IsUniqueViolation(Saved(exception)).ShouldBeFalse($"{provider.Name}: saved {exception.GetType().Name}");
            }
        }
    }
}
