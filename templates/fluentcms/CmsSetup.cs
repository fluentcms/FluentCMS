using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Schema;
#if (sqlite)
using FluentCMS.Data.Sqlite;
#elseif (postgresql)
using FluentCMS.Data.PostgreSql;
#else
using FluentCMS.Data.SqlServer;
#endif
using FluentCMS.Plugins.Media;
using FluentCMS.Plugins.Webhooks;
using FluentCMS.Schema;
#if (blog)
using FluentCmsApp.Blog;
#endif

namespace FluentCmsApp;

// What the host and the design-time factory of `fluentcms migrations add` share, so that both see the same model.
public static class CmsSetup
{
    public const string ConnectionStringName = "FluentCms";

    // Every class of this project marked [Collection], [Singleton], [Component] or [Block] is a content type.
    public static SchemaModel BuildSchema(IFieldTypeRegistry fieldTypes) =>
        new SchemaBuilder(fieldTypes).AddContentTypesFromAssembly(typeof(CmsSetup).Assembly).Build();

    public static void Configure(FluentCmsBuilder cms, string? connectionString)
    {
#if (sqlite)
        cms.UseSqlite(connectionString);
#elseif (postgresql)
        cms.UsePostgreSql(connectionString);
#else
        cms.UseSqlServer(connectionString);
#endif
        cms.AddPlugin<MediaPlugin>();
        cms.AddPlugin<WebhooksPlugin>();
#if (blog)
        cms.AddBlogSample();
#endif
    }
}
