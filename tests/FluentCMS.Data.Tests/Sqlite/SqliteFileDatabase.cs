using FluentCMS.Core.DependencyInjection;
using FluentCMS.Data.Sqlite;
using FluentCMS.Schema;
using Microsoft.Extensions.DependencyInjection;

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

    public void Dispose() => _directory.Delete(recursive: true);
}
