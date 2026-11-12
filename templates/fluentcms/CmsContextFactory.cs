using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Storage;
#if (sqlite)
using FluentCMS.Data.Sqlite;
#elseif (postgresql)
using FluentCMS.Data.PostgreSql;
#else
using FluentCMS.Data.SqlServer;
#endif
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore.Design;

namespace FluentCmsApp;

// Used by `fluentcms migrations add` to write the content migrations of Migrations/Content for this host's provider. It never
// opens the database named here. The field types come from the host's services, so that a plugin's type, such as media, is known.
public sealed class CmsContextFactory : IDesignTimeDbContextFactory<CmsDbContext>
{
#if (sqlite)
    private const string NeverOpened = "Data Source=:memory:";
#elseif (postgresql)
    private const string NeverOpened = "Host=localhost;Database=never_opened";
#else
    private const string NeverOpened = "Server=localhost;Database=never_opened";
#endif

    public CmsDbContext CreateDbContext(string[] args)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddFluentCms(cms => CmsSetup.Configure(cms, NeverOpened));
        using var provider = services.BuildServiceProvider();

        var schema = CmsSetup.BuildSchema(provider.GetRequiredService<IFieldTypeRegistry>());
#if (sqlite)
        return ContentMigrations.CreateContext(new SqliteDataProvider(NeverOpened), schema, typeof(CmsContextFactory).Assembly);
#elseif (postgresql)
        return ContentMigrations.CreateContext(new PostgreSqlDataProvider(NeverOpened), schema, typeof(CmsContextFactory).Assembly);
#else
        return ContentMigrations.CreateContext(new SqlServerDataProvider(NeverOpened), schema, typeof(CmsContextFactory).Assembly);
#endif
    }
}
