using System.Reflection;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Storage;
using FluentCMS.Data.Sqlite;
using FluentCMS.Schema;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FluentCMS.Data.Tests.Sqlite;

// A database file of its own, reached through the real package. Pooling is off so that the file can be deleted.
internal sealed class SqliteFileDatabase : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("fluentcms-data-tests-");

    public string ConnectionString => $"Data Source={Path.Combine(_directory.FullName, "cms.db")};Pooling=False";

    public ServiceProvider CreateServices(SchemaModel? schema = null)
    {
        var services = new ServiceCollection();
        if (schema is not null)
        {
            services.AddSingleton(schema);
        }

        services.AddFluentCms(cms => cms.UseSqlite(ConnectionString));
        return services.BuildServiceProvider();
    }

    // A real host, so that startup runs the way it does in an application. Nothing is started yet.
    public IHost CreateHost(
        SchemaModel schema,
        string environment,
        Assembly? contentMigrations = null,
        Dictionary<string, string?>? settings = null,
        Action<IServiceCollection>? services = null)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = environment });
        builder.Configuration.AddInMemoryCollection(settings ?? []);
        builder.Services.AddSingleton(schema);
        services?.Invoke(builder.Services);
        builder.Services.AddFluentCms(cms => cms
            .UseSqlite(ConnectionString)
            .UseContentMigrations(contentMigrations ?? typeof(CmsDbContext).Assembly));

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
}
