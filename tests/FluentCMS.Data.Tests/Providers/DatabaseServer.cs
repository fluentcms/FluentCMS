using System.Data.Common;
using System.Reflection;
using DotNet.Testcontainers.Containers;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Data.PostgreSql;
using FluentCMS.Data.SqlServer;
using FluentCMS.Data.Tests.Providers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Xunit;

[assembly: AssemblyFixture(typeof(DatabaseServer))]

namespace FluentCMS.Data.Tests.Providers;

// The one server of a run on PostgreSQL or SQL Server, in a container, on which every test gets a database of its own.
// A SQLite run starts nothing.
public sealed class DatabaseServer : IAsyncLifetime
{
    // postgres:15.19, the multi-arch index.
    private const string PostgreSqlImage = "postgres@sha256:1aac5b49cefd627f02816141ca4ad012d6cc19460dfe02a9b5168a105be3fc53";

    // mcr.microsoft.com/mssql/server:2019-CU32-GDR12-ubuntu-20.04, amd64 only.
    private const string SqlServer2019Image = "mcr.microsoft.com/mssql/server@sha256:07d7dedecdbfcf6776035dc5eefb5bfd9aa1804b2f6b99f7dedb397d8a599069";

    // mcr.microsoft.com/mssql/server:2025-CU10-ubuntu-24.04, amd64 only.
    private const string SqlServer2025Image = "mcr.microsoft.com/mssql/server@sha256:245f6b1c8903edae52cb20fa31419cefcde1a49938bb8c3447099011fc871ca3";

    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(10);

    private static string? _adminConnectionString;

    private IDatabaseContainer? _container;

    public async ValueTask InitializeAsync()
    {
        if (TestProvider.IsSqlite)
        {
            return;
        }

        // The data is thrown away with the container, so the server need not flush it, as SqliteFileDatabase does not.
        _container = TestProvider.Name switch
        {
            TestProvider.PostgreSql => new PostgreSqlBuilder(PostgreSqlImage)
                .WithCommand("-c", "fsync=off", "-c", "synchronous_commit=off", "-c", "full_page_writes=off", "-c", "max_connections=500")
                .Build(),
            TestProvider.SqlServer2019 => new MsSqlBuilder(SqlServer2019Image).Build(),
            _ => new MsSqlBuilder(SqlServer2025Image).Build(),
        };

        using var timeout = new CancellationTokenSource(StartTimeout);
        await _container.StartAsync(timeout.Token);
        _adminConnectionString = _container.GetConnectionString();

        // SQL Server's way of not flushing: a commit does not wait for the log. Every database it creates takes it from model.
        if (TestProvider.IsSqlServer)
        {
            Run(new SqlConnection(_adminConnectionString), "ALTER DATABASE model SET DELAYED_DURABILITY = FORCED");
            SqlServerPool.Start(_adminConnectionString);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _adminConnectionString = null;
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    // forSystemTables: see TestDatabase.Create.
    internal static TestDatabase NewDatabase(bool forSystemTables)
    {
        var admin = _adminConnectionString ?? throw new InvalidOperationException($"{TestProvider.Variable} is {TestProvider.Name}, and its container has not started.");
        if (TestProvider.IsSqlServer)
        {
            var taken = SqlServerPool.Take(admin, forSystemTables);
            return new ServerDatabase(taken, new SqlConnectionStringBuilder(admin) { InitialCatalog = taken }.ToString());
        }

        var name = $"t_{Guid.NewGuid():N}";
        Run(new NpgsqlConnection(admin), $"CREATE DATABASE \"{name}\"");

        // Error details name the values of a failed statement, which a failure list needs.
        return new ServerDatabase(name, new NpgsqlConnectionStringBuilder(admin) { Database = name, IncludeErrorDetail = true }.ToString());
    }

    internal static void DropDatabase(string name)
    {
        var admin = _adminConnectionString ?? throw new InvalidOperationException($"The {TestProvider.Name} container has stopped.");
        if (TestProvider.IsSqlServer)
        {
            SqlServerPool.Release(admin, name);
            return;
        }

        Run(new NpgsqlConnection(admin), $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");
    }

    private static void Run(DbConnection connection, string sql)
    {
        using (connection)
        {
            connection.Open();
            using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Names of the tests' own databases.
            command.CommandText = sql;
#pragma warning restore CA2100
            command.ExecuteNonQuery();
        }
    }
}

// A database on the server of the run, dropped when the test ends.
internal sealed class ServerDatabase(string name, string connectionString) : TestDatabase
{
    private readonly string _schema = TestProvider.IsPostgreSql ? "current_schema()" : "SCHEMA_NAME()";

    public override string ConnectionString => connectionString;

    public override void UseProvider(FluentCmsBuilder cms)
    {
        if (TestProvider.IsPostgreSql)
        {
            cms.UsePostgreSql(connectionString);
        }
        else
        {
            cms.UseSqlServer(connectionString);
        }
    }

    public override void UseDriver(DbContextOptionsBuilder options, Assembly migrationsAssembly, string historyTable)
    {
        if (TestProvider.IsPostgreSql)
        {
            options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(migrationsAssembly).MigrationsHistoryTable(historyTable));
        }
        else
        {
            options.UseSqlServer(connectionString, sqlServer => sqlServer.MigrationsAssembly(migrationsAssembly).MigrationsHistoryTable(historyTable));
        }
    }

    public override List<string> Tables() =>
        ReadStrings($"select table_name from information_schema.tables where table_schema = {_schema} and table_type = 'BASE TABLE' order by {Binary("table_name")}");

    public override List<string> Columns(string table) =>
        ReadStrings($"select column_name from information_schema.columns where table_schema = {_schema} and table_name = '{table}' order by {Binary("column_name")}");

    public override void Dispose() => DatabaseServer.DropDatabase(name);

    // A host on a server migrates at start only when told to, and these hosts are the ones that are told, so that a
    // test sees the streams applied as it does on SQLite. A test that sets the option itself still wins.
    public override Dictionary<string, string?> HostSettings => new() { ["FluentCms:Database:MigrateOnStartup"] = "true" };

    protected override DbConnection NewConnection() =>
        TestProvider.IsPostgreSql ? new NpgsqlConnection(connectionString) : new SqlConnection(connectionString);

    // Binary order, as SQLite sorts the names.
    private static string Binary(string column) =>
        TestProvider.IsPostgreSql ? $"{column}::text collate \"C\"" : $"{column} collate Latin1_General_BIN2";
}
