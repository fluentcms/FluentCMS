using System.Reflection;
using FluentCMS.Core.Storage;
using FluentCMS.Data.PostgreSql;
using FluentCMS.Data.Sqlite;
using FluentCMS.Data.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
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

    internal static PostgresException PostgreSqlError(string sqlState) => new("duplicate key value", "ERROR", "ERROR", sqlState);

    internal static PostgresException PostgreSqlError(string sqlState, string constraint, string detail) =>
        new($"duplicate key value violates unique constraint \"{constraint}\"", "ERROR", "ERROR", sqlState, detail: detail, constraintName: constraint);

    // SqlClient has no public way to make one, so this takes the internal path the driver itself uses.
    internal static SqlException SqlServerError(int number, string message = "")
    {
        var constructor = typeof(SqlError).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).MaxBy(candidate => candidate.GetParameters().Length)!;
        var parameters = constructor.GetParameters();
        var arguments = parameters
            .Select(parameter => parameter.ParameterType == typeof(string) ? "" : parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null)
            .ToArray();
        arguments[0] = number;
        arguments[Array.FindIndex(parameters, parameter => parameter.Name == "errorMessage")] = message;
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

    // The key of the token table as the system model names it, on a model of its own (#3447).
    private static readonly IUniqueConstraint TokenKey = KeyOf<TokenRow>();

    private const string TokenTable = "create table cms_user_token (user_id text, login_provider text, name text, value text, constraint PK_cms_user_token primary key (user_id, login_provider, name))";

    [Fact]
    public void SqliteKnowsAKeyByItsColumns()
    {
        Sqlite.IsUniqueViolation(Saved(SqliteError(TokenTable, "insert into cms_user_token values ('u', 'p', 'n', 'v')")), TokenKey).ShouldBeTrue();
    }

    [Theory]
    [InlineData(TokenTable + "; create unique index ux_two on cms_user_token (user_id, login_provider); insert into cms_user_token values ('u', 'p', 'n', 'v')", "insert into cms_user_token values ('u', 'p', 'm', 'v')")]
    [InlineData("create table x_cms_user_token (user_id text, login_provider text, name text, constraint PK_x primary key (user_id, login_provider, name)); insert into x_cms_user_token values ('u', 'p', 'n')", "insert into x_cms_user_token values ('u', 'p', 'n')")]
    [InlineData("create table cms_user_login (login_provider text, provider_key text, constraint PK_cms_user_login primary key (login_provider, provider_key)); insert into cms_user_login values ('cms_user_token', 'PK_cms_user_token')", "insert into cms_user_login values ('cms_user_token', 'PK_cms_user_token')")]
    public void SqliteTellsAnotherKeyApart(string setup, string failing)
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        Run(connection, setup);
        var error = Should.Throw<SqliteException>(() => Run(connection, failing));

        Sqlite.IsUniqueViolation(error).ShouldBeTrue();
        Sqlite.IsUniqueViolation(Saved(error), TokenKey).ShouldBeFalse(error.Message);
    }

    // Predictions of the server's error: the constraint is a field of its own, apart from the duplicate value.
    [Fact]
    public void PostgreSqlKnowsAKeyByItsConstraintName()
    {
        PostgreSql.IsUniqueViolation(Saved(PostgreSqlError(PostgresErrorCodes.UniqueViolation, "PK_cms_user_token", "Key (user_id, login_provider, name)=(u, p, n) already exists.")), TokenKey).ShouldBeTrue();
        PostgreSql.IsUniqueViolation(Saved(PostgreSqlError(PostgresErrorCodes.UniqueViolation, "PK_cms_user_login", "Key (login_provider, provider_key)=(cms_user_token, PK_cms_user_token) already exists.")), TokenKey).ShouldBeFalse();
        PostgreSql.IsUniqueViolation(Saved(PostgreSqlError(PostgresErrorCodes.ForeignKeyViolation, "PK_cms_user_token", "")), TokenKey).ShouldBeFalse();
    }

    // Predictions of the server's messages: 2627 quotes the constraint and its table, 2601 the table and its index, and the
    // duplicate value comes last, where it may repeat either name.
    [Theory]
    [InlineData(2627, "Violation of PRIMARY KEY constraint 'PK_cms_user_token'. Cannot insert duplicate key in object 'dbo.cms_user_token'. The duplicate key value is (0198a3c4-0000-7000-8000-000000000001, [AspNetUserStore], SignedInFailures).", true)]
    [InlineData(2601, "Cannot insert duplicate key row in object 'dbo.cms_user_token' with unique index 'PK_cms_user_token'. The duplicate key value is (u, p, n).", true)]
    [InlineData(2627, "Violation of PRIMARY KEY constraint 'PK_cms_user_login'. Cannot insert duplicate key in object 'dbo.cms_user_login'. The duplicate key value is (cms_user_token, 'PK_cms_user_token').", false)]
    [InlineData(2601, "Cannot insert duplicate key row in object 'dbo.cms_user' with unique index 'ix_cms_user_normalized_email'. The duplicate key value is ('PK_cms_user_token').", false)]
    [InlineData(2627, "Verletzung der PRIMARY KEY-Einschränkung „PK_cms_user_token“. Ein doppelter Schlüssel kann in das dbo.cms_user_token-Objekt nicht eingefügt werden.", false)]
    [InlineData(547, "The INSERT statement conflicted with the FOREIGN KEY constraint 'PK_cms_user_token'.", false)]
    public void SqlServerKnowsAKeyByTheNameItQuotesBeforeTheValue(int number, string message, bool token)
    {
        SqlServer.IsUniqueViolation(Saved(SqlServerError(number, message)), TokenKey).ShouldBe(token);
    }

    [Fact]
    public void NoProviderTakesAnotherProvidersKeyOrAPlainFailure()
    {
        var sqlite = SqliteError(TokenTable, "insert into cms_user_token values ('u', 'p', 'n', 'v')");
        var postgreSql = PostgreSqlError(PostgresErrorCodes.UniqueViolation, "PK_cms_user_token", "");
        var sqlServer = SqlServerError(2627, "Violation of PRIMARY KEY constraint 'PK_cms_user_token'. Cannot insert duplicate key in object 'dbo.cms_user_token'. The duplicate key value is (u, p, n).");

        foreach (var (provider, foreign) in new (IDataProvider, Exception[])[] { (Sqlite, [postgreSql, sqlServer]), (PostgreSql, [sqlite, sqlServer]), (SqlServer, [sqlite, postgreSql]) })
        {
            foreach (var exception in foreign.Concat([new InvalidOperationException("cms_user_token PK_cms_user_token"), Saved(new InvalidOperationException("Inner."))]))
            {
                provider.IsUniqueViolation(Saved(exception), TokenKey).ShouldBeFalse($"{provider.Name}: {exception.GetType().Name}");
            }
        }
    }

    private static IUniqueConstraint KeyOf<T>()
        where T : class
    {
        using var context = new Keys(new DbContextOptionsBuilder<Keys>().UseSqlite("Data Source=:memory:").Options);
        return context.Model.FindEntityType(typeof(T))!.FindPrimaryKey()!.GetMappedConstraints().Single();
    }

    private sealed class TokenRow
    {
        public string UserId { get; set; } = "";

        public string LoginProvider { get; set; } = "";

        public string Name { get; set; } = "";
    }

    private sealed class Keys(DbContextOptions<Keys> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<TokenRow>(token =>
            {
                token.ToTable("cms_user_token");
                token.HasKey(row => new { row.UserId, row.LoginProvider, row.Name }).HasName("PK_cms_user_token");
                token.Property(row => row.UserId).HasColumnName("user_id");
                token.Property(row => row.LoginProvider).HasColumnName("login_provider");
                token.Property(row => row.Name).HasColumnName("name");
            });
    }

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
