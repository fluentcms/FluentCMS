using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Schema;
using FluentCMS.Data.PostgreSql;
using FluentCMS.Data.Sqlite;
using FluentCMS.Data.SqlServer;
using FluentCMS.Samples.Blog;
using FluentCMS.Schema;

namespace FluentCMS.Host;

// The application of the Docker image, set up from configuration alone, so environment variables are enough.
public static class ReferenceHost
{
    public const string SchemaVariable = "FLUENTCMS_SCHEMA";
    public const string SampleVariable = "FLUENTCMS_SAMPLE";
    public const string ProviderKey = "FluentCms:Database:Provider";
    public const string ConnectionStringName = "FluentCms";
    public const string BlogSampleName = "blog";

    public static WebApplication Build(WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var configuration = builder.Configuration;
        var blog = UsesBlogSample(configuration[SampleVariable]);
        var file = configuration[SchemaVariable];

        builder.Services.AddSingleton(BuildSchema(file, blog));
        builder.Services.AddFluentCms(cms =>
        {
            UseDatabase(cms, configuration, builder.Environment);
            cms.UseContentMigrations(typeof(ReferenceHost).Assembly);

            // Types that are data have no compiled migrations, so their tables follow the schema at start (Q2, until the
            // CLI generates migrations from a schema file). Core refuses this on a provider without schema sync.
            if (blog || !string.IsNullOrWhiteSpace(file))
            {
                cms.AllowSchemaSyncOutsideDevelopment();
            }

            if (blog)
            {
                cms.AddBlogSample();
            }
        });

        var app = builder.Build();
        app.UseFluentCms();
        app.UseAuthorization();
        app.MapFluentCms();
        return app;
    }

    private static bool UsesBlogSample(string? sample) => sample switch
    {
        null or "" => false,
        _ when string.Equals(sample, BlogSampleName, StringComparison.OrdinalIgnoreCase) => true,
        _ => throw new ReferenceHostException(ReferenceHostErrorCodes.UnknownSample, $"{SampleVariable} is '{sample}'; the one sample is '{BlogSampleName}'."),
    };

    // A file that is there but invalid stops the host with the FCMS12xx codes of SchemaBuilder.
    private static SchemaModel BuildSchema(string? file, bool blog)
    {
        var schema = new SchemaBuilder();
        if (blog)
        {
            foreach (var type in BlogSample.ContentTypes)
            {
                schema.AddContentType(type);
            }
        }

        if (!string.IsNullOrWhiteSpace(file))
        {
            if (!File.Exists(file))
            {
                throw new ReferenceHostException(ReferenceHostErrorCodes.SchemaFileNotFound, $"{SchemaVariable} names {file}, which does not exist or cannot be read.");
            }

            schema.LoadSchemaFile(file);
        }

        return schema.Build();
    }

    private static void UseDatabase(FluentCmsBuilder cms, ConfigurationManager configuration, IWebHostEnvironment environment)
    {
        var provider = configuration[ProviderKey];
        var connectionString = configuration.GetConnectionString(ConnectionStringName);

        if (string.IsNullOrWhiteSpace(provider) || Is(provider, "Sqlite"))
        {
            cms.UseSqlite(string.IsNullOrWhiteSpace(connectionString) ? $"Data Source={Path.Combine(environment.ContentRootPath, "fluentcms.db")}" : connectionString);
        }
        else if (Is(provider, "PostgreSql"))
        {
            cms.UsePostgreSql(Required(connectionString, provider));
        }
        else if (Is(provider, "SqlServer"))
        {
            cms.UseSqlServer(Required(connectionString, provider));
        }
        else
        {
            throw new ReferenceHostException(ReferenceHostErrorCodes.UnknownDataProvider, $"{ProviderKey} is '{provider}'; use Sqlite, PostgreSql or SqlServer.");
        }
    }

    private static bool Is(string provider, string name) => string.Equals(provider, name, StringComparison.OrdinalIgnoreCase);

    private static string Required(string? connectionString, string provider) =>
        string.IsNullOrWhiteSpace(connectionString)
            ? throw new ReferenceHostException(ReferenceHostErrorCodes.NoConnectionString, $"{provider} needs ConnectionStrings:{ConnectionStringName}.")
            : connectionString;
}
