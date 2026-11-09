using System.Data.Common;
using System.Globalization;
using System.Reflection;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Storage;
using FluentCMS.Data.Tests.Sqlite;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FluentCMS.Data.Tests.Providers;

// A database of a test's own on the provider of the run: a SQLite file, or a new database on the server of DatabaseServer.
internal abstract class TestDatabase : IDisposable
{
    public abstract string ConnectionString { get; }

    public static TestDatabase Create() => TestProvider.IsSqlite ? new SqliteFileDatabase() : DatabaseServer.NewDatabase();

    // The provider as a host registers it.
    public abstract void UseProvider(FluentCmsBuilder cms);

    // The provider's driver alone, for a context of the tests' own such as a demo plugin's.
    public abstract void UseDriver(DbContextOptionsBuilder options, Assembly migrationsAssembly, string historyTable);

    // A server applies its system migrations, as a host does.
    public virtual void CreateSystemTables(SystemDbContext context) => context.Database.Migrate();

    // Only a SQLite file has a journal mode; see TestProvider.JournalModes.
    public virtual void UseJournalMode(string journalMode)
    {
    }

    // Configuration every host of this provider starts with, before the test's own settings.
    public virtual Dictionary<string, string?> HostSettings => [];

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
            UseProvider(cms);
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
        // These hosts sign no tokens and send no links, so Production may start with the key the authorization server would
        // generate, no public origin and the key ring unencrypted.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FluentCms:Security:AllowGeneratedSigningKey"] = "true",
            ["FluentCms:Security:AllowRequestOrigin"] = "true",
            ["FluentCms:Security:AllowUnencryptedKeyRing"] = "true",
        });
        builder.Configuration.AddInMemoryCollection(HostSettings);
        builder.Configuration.AddInMemoryCollection(settings ?? []);
        builder.Services.AddSingleton(schema);
        services?.Invoke(builder.Services);
        builder.Services.AddFluentCms(cms =>
        {
            UseProvider(cms);
            cms.UseContentMigrations(contentMigrations ?? typeof(CmsDbContext).Assembly);

            // Their outbox handlers start a schedule in the background, which a test that disposes the host without stopping it leaves running against a database it then deletes.
            cms.Activity.Enabled = false;
            cms.DeliveryCache.OutputCache = false;
            configure?.Invoke(cms);
        });

        return builder.Build();
    }

    public abstract List<string> Tables();

    public abstract List<string> Columns(string table);

    public List<string> ReadStrings(string sql)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Constants of the tests.
        command.CommandText = sql;
#pragma warning restore CA2100
        using var reader = command.ExecuteReader();

        var values = new List<string>();
        while (reader.Read())
        {
            values.Add(Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture)!);
        }

        return values;
    }

    public void Execute(string sql)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Constants of the tests.
        command.CommandText = sql;
#pragma warning restore CA2100
        command.ExecuteNonQuery();
    }

    public abstract void Dispose();


    protected abstract DbConnection NewConnection();

    private DbConnection OpenConnection()
    {
        var connection = NewConnection();
        connection.Open();
        return connection;
    }
}
