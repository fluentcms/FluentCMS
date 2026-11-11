using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Schema;
using FluentCMS.Data.PostgreSql;
using FluentCMS.Data.Sqlite;
using FluentCMS.Data.SqlServer;
using FluentCMS.Plugins.AiAssist;
using FluentCMS.Plugins.GraphQL;
using FluentCMS.Plugins.Mcp;
using FluentCMS.Plugins.Media;
using FluentCMS.Plugins.MultiSite;
using FluentCMS.Plugins.Webhooks;
using FluentCMS.Samples.Blog;
using FluentCMS.Schema;
using Microsoft.AspNetCore.DataProtection;

namespace FluentCMS.Host;

// The application of the Docker image, set up from configuration alone, so environment variables are enough.
public static class ReferenceHost
{
    public const string SchemaVariable = "FLUENTCMS_SCHEMA";
    public const string SampleVariable = "FLUENTCMS_SAMPLE";
    public const string ProviderKey = "FluentCms:Database:Provider";
    public const string ConnectionStringName = "FluentCms";
    public const string BlogSampleName = "blog";
    public const string KeyRingCertificatePathKey = "FluentCms:DataProtection:Certificate:Path";
    public const string KeyRingCertificatePasswordKey = "FluentCms:DataProtection:Certificate:Password";

    public static WebApplication Build(WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var configuration = builder.Configuration;
        var blog = UsesBlogSample(configuration[SampleVariable]);
        var file = configuration[SchemaVariable];

        // Built with the registry of the container, so that a schema file can use the field types of plugins, such as media.
        builder.Services.AddSingleton(services => BuildSchema(file, blog, services.GetRequiredService<IFieldTypeRegistry>()));
        ProtectKeyRing(builder.Services, configuration);
        builder.Services.AddFluentCms(cms =>
        {
            UseDatabase(cms, configuration, builder.Environment);
            cms.UseContentMigrations(typeof(ReferenceHost).Assembly);
            cms.AddPlugin<MediaPlugin>();
            cms.AddPlugin<WebhooksPlugin>();
            cms.AddPlugin<McpPlugin>();
            cms.AddPlugin<MultiSitePlugin>();
            cms.AddPlugin<GraphQLPlugin>();
            cms.AddPlugin<AiAssistPlugin>();

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

        // Now rather than at first use, so that an invalid schema file stops the host before it starts.
        app.Services.GetRequiredService<SchemaModel>();
        app.UseFluentCms();
        app.MapFluentCms();

        // Off outside Development unless FluentCms:OpenApi:Enabled is true, since the document names every type and field.
        app.MapFluentCmsOpenApi();
        return app;
    }

    private static bool UsesBlogSample(string? sample) => sample?.Trim() switch
    {
        null or "" => false,
        var name when string.Equals(name, BlogSampleName, StringComparison.OrdinalIgnoreCase) => true,
        _ => throw new ReferenceHostException(ReferenceHostErrorCodes.UnknownSample, $"{SampleVariable} is '{sample}'; the one sample is '{BlogSampleName}'."),
    };

    // A file that is there but invalid stops the host with the FCMS12xx codes of SchemaBuilder.
    private static SchemaModel BuildSchema(string? file, bool blog, IFieldTypeRegistry fieldTypes)
    {
        var schema = new SchemaBuilder(fieldTypes);
        if (blog)
        {
            foreach (var type in BlogSample.ContentTypes)
            {
                schema.AddContentType(type);
            }
        }

        if (!string.IsNullOrWhiteSpace(file))
        {
            try
            {
                schema.LoadSchemaFile(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new ReferenceHostException(ReferenceHostErrorCodes.SchemaFileNotFound, $"{SchemaVariable} names {file}, which does not exist or cannot be read: {exception.Message}");
            }
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

    // The image is set up by environment variables alone, so it cannot call ProtectKeysWithCertificate the way a host of
    // its own does. Before AddFluentCms, which keeps the ring loading on first use rather than at start.
    private static void ProtectKeyRing(IServiceCollection services, ConfigurationManager configuration)
    {
        var path = configuration[KeyRingCertificatePathKey];
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        X509Certificate2 certificate;
        try
        {
            certificate = X509CertificateLoader.LoadPkcs12FromFile(path, configuration[KeyRingCertificatePasswordKey]);
        }
        catch (Exception exception) when (exception is CryptographicException or IOException or UnauthorizedAccessException)
        {
            throw new ReferenceHostException(ReferenceHostErrorCodes.KeyRingCertificateUnusable, $"{KeyRingCertificatePathKey} is '{path}', which cannot be read as a PKCS#12 file with this password: {exception.Message}");
        }

        // Data Protection encrypts the ring for the certificate's RSA key and needs the private key to read it back.
        using (var rsa = certificate.GetRSAPrivateKey())
        {
            if (rsa is null)
            {
                certificate.Dispose();
                throw new ReferenceHostException(ReferenceHostErrorCodes.KeyRingCertificateUnusable, $"{KeyRingCertificatePathKey} is '{path}', which holds no RSA private key.");
            }
        }

        services.AddDataProtection().ProtectKeysWithCertificate(certificate);
    }

    private static bool Is(string provider, string name) => string.Equals(provider, name, StringComparison.OrdinalIgnoreCase);

    private static string Required(string? connectionString, string provider) =>
        string.IsNullOrWhiteSpace(connectionString)
            ? throw new ReferenceHostException(ReferenceHostErrorCodes.NoConnectionString, $"{provider} needs ConnectionStrings:{ConnectionStringName}.")
            : connectionString;
}
