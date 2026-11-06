using System.Data.Common;
using System.Reflection;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Storage;
using FluentCMS.Data.Sqlite;
using FluentCMS.Schema;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FluentCMS.Data.Tests.Sqlite;

// A database file of its own, reached through the real package. Pooling is off so that the file can be deleted.
internal sealed class SqliteFileDatabase : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("fluentcms-data-tests-");

    public string ConnectionString => $"Data Source={Path.Combine(_directory.FullName, "cms.db")};Pooling=False";

    public void UseSqlite(FluentCmsBuilder cms) => cms.UseDataProvider(new UnflushedSqliteProvider(new SqliteDataProvider(ConnectionString)));

    public ServiceProvider CreateServices(SchemaModel? schema = null, Action<FluentCmsBuilder>? configure = null)
    {
        var services = new ServiceCollection();
        if (schema is not null)
        {
            services.AddSingleton(schema);
        }

        // These tests name a principal's roles in its roles claim. A test of stored users registers UserPrincipalRoles in configure.
        services.AddSingleton<IPrincipalRoles, ClaimPrincipalRoles>();
        services.AddFluentCms(cms =>
        {
            UseSqlite(cms);
            cms.Activity.Enabled = false;
            cms.DeliveryCache.OutputCache = false;
            configure?.Invoke(cms);
        });
        return services.BuildServiceProvider();
    }

    // A real host, so that startup runs the way it does in an application. Nothing is started yet.
    public IHost CreateHost(
        SchemaModel schema,
        string environment,
        Assembly? contentMigrations = null,
        Dictionary<string, string?>? settings = null,
        Action<IServiceCollection>? services = null,
        Action<FluentCmsBuilder>? configure = null)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = environment });
        // These hosts sign no tokens, so Production may start with the key the authorization server would generate.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["FluentCms:Security:AllowGeneratedSigningKey"] = "true" });
        builder.Configuration.AddInMemoryCollection(settings ?? []);
        builder.Services.AddSingleton(schema);
        services?.Invoke(builder.Services);
        builder.Services.AddFluentCms(cms =>
        {
            UseSqlite(cms);
            cms.UseContentMigrations(contentMigrations ?? typeof(CmsDbContext).Assembly);

            // Their outbox handlers start a schedule in the background, which a test that disposes the host without stopping it leaves running against a file it then deletes.
            cms.Activity.Enabled = false;
            cms.DeliveryCache.OutputCache = false;
            configure?.Invoke(cms);
        });

        return builder.Build();
    }

    public List<string> Tables() =>
        ReadStrings("select name from sqlite_master where type = 'table' and name not like 'sqlite_%' order by name");

    public List<string> Columns(string table) =>
        ReadStrings($"select name from pragma_table_info('{table}') order by name");

    public List<string> ReadStrings(string sql)
    {
        using var connection = new SqliteConnection(ConnectionString);
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

    public void Execute(string sql)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Constants of the tests.
        command.CommandText = sql;
#pragma warning restore CA2100
        command.ExecuteNonQuery();
    }

    public void Dispose() => _directory.Delete(recursive: true);

    // A commit of a file database waits for the disk to flush, and the tests seed rows and create tables one commit at a time, which is most of what they cost on a Windows runner. The locks behave the same without it, and the file is deleted after the test.
    private sealed class UnflushedSqliteProvider(SqliteDataProvider inner) : IDataProvider
    {
        private static readonly NoFlush Interceptor = new();

        public string Name => inner.Name;

        public bool MigrateOnStartup => inner.MigrateOnStartup;

        public bool SupportsSchemaSync => inner.SupportsSchemaSync;

        public IStorageConventions Conventions => inner.Conventions;

        public IJsonSqlDialect JsonDialect => inner.JsonDialect;

        public IJobClaimDialect JobClaimDialect => inner.JobClaimDialect;

        public IDatabaseSchemaReader SchemaReader => inner.SchemaReader;

        public void ConfigureContentMigrations(DbContextOptionsBuilder options, Assembly migrationsAssembly)
        {
            inner.ConfigureContentMigrations(options, migrationsAssembly);
            options.AddInterceptors(Interceptor);
        }

        public void ConfigureSystemMigrations(DbContextOptionsBuilder options)
        {
            inner.ConfigureSystemMigrations(options);
            options.AddInterceptors(Interceptor);
        }
    }

    private sealed class NoFlush : DbConnectionInterceptor
    {
        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) => Off(connection);

        public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            Off(connection);
            return Task.CompletedTask;
        }

        private static void Off(DbConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA synchronous = OFF";
            command.ExecuteNonQuery();
        }
    }
}
