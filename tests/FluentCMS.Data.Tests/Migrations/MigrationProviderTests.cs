using System.Reflection;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Storage;
using FluentCMS.Data.PostgreSql;
using FluentCMS.Data.Sqlite;
using FluentCMS.Data.SqlServer;
using FluentCMS.Data.Tests.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Migrations;

// A set scaffolded for one provider and run on another. PostgreSQL and SQL Server never open a connection here (#2711):
// the check reads the migrations and the snapshot only.
public sealed class MigrationProviderTests : IDisposable
{
    private const string Sqlite = "SQLite";
    private const string PostgreSql = "PostgreSQL";
    private const string SqlServer = "SQL Server";

    private readonly SqliteFileDatabase _database = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _database.Dispose();

    [Theory]
    [InlineData(PostgreSql)]
    [InlineData(SqlServer)]
    public async Task AServerHostRefusesASqliteContentSetBeforeTouchingTheDatabase(string provider)
    {
        using var host = ServerHost(provider, GeneratedMigrations.CompileFirst(ArticleSchemas.Titled, provider: Provider(Sqlite)));

        var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => host.StartAsync(Token));
        var migrate = await Should.ThrowAsync<FluentCmsConfigurationException>(() => host.Services.GetRequiredService<MigrationRunner>().MigrateAsync(Token));

        exception.Code.ShouldBe("FCMS1306");
        exception.Message.ShouldBe($"FCMS1306: stream content was scaffolded for SQLite; this host runs {provider}, and a migration set applies only to the provider it was scaffolded for");
        migrate.Message.ShouldBe(exception.Message);
    }

    [Fact]
    public async Task ASqliteHostRefusesAPostgreSqlContentSetAndAppliesNothing()
    {
        using var host = _database.CreateHost(ArticleSchemas.Titled, Environments.Production, GeneratedMigrations.CompileFirst(ArticleSchemas.Titled, provider: Provider(PostgreSql)));

        var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => host.StartAsync(Token));

        exception.Message.ShouldStartWith("FCMS1306: stream content was scaffolded for PostgreSQL; this host runs SQLite");
        _database.Tables().ShouldBeEmpty();
    }

    [Fact]
    public async Task SchemaSyncLeavesTheContentSetOfAServerProviderAlone()
    {
        using var host = _database.CreateHost(ArticleSchemas.Titled, Environments.Development, GeneratedMigrations.CompileFirst(ArticleSchemas.Titled, provider: Provider(PostgreSql)));

        await host.StartAsync(Token);

        _database.Tables().ShouldContain("cms_article");
        _database.Tables().ShouldNotContain("cms_content__migrations");
        await host.StopAsync(Token);
    }

    [Theory]
    [InlineData(Sqlite)]
    [InlineData(PostgreSql)]
    [InlineData(SqlServer)]
    public void TheSetsScaffoldedForAProviderAreItsOwn(string provider)
    {
        using var services = Services(provider, GeneratedMigrations.CompileFirst(ArticleSchemas.Titled, provider: Provider(provider)));
        using var scope = services.CreateScope();

        MigrationProviders.ForeignTo(scope.ServiceProvider.GetRequiredService<CmsDbContext>()).ShouldBeNull();
        MigrationProviders.ForeignTo(scope.ServiceProvider.GetRequiredService<SystemDbContext>()).ShouldBeNull();
    }

    [Fact]
    public void AHandWrittenMigrationWithoutATargetModelNamesNoProvider()
    {
        var options = new DbContextOptionsBuilder<DemoPluginDbContext>()
            .UseNpgsql("Host=localhost;Database=never_opened", npgsql => npgsql.MigrationsAssembly(typeof(DemoPluginDbContext).Assembly))
            .Options;
        using var context = new DemoPluginDbContext(options);

        MigrationProviders.ForeignTo(context).ShouldBeNull();
    }

    private static IDataProvider Provider(string name) => name switch
    {
        Sqlite => new SqliteDataProvider("Data Source=:memory:"),
        PostgreSql => new PostgreSqlDataProvider("Host=localhost;Database=never_opened"),
        _ => new SqlServerDataProvider("Server=localhost;Database=never_opened"),
    };

    private static ServiceProvider Services(string provider, Assembly contentMigrations) =>
        new ServiceCollection()
            .AddSingleton(ArticleSchemas.Titled)
            .AddFluentCms(cms => cms.UseDataProvider(Provider(provider)).UseContentMigrations(contentMigrations))
            .BuildServiceProvider();

    private static IHost ServerHost(string provider, Assembly contentMigrations)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = Environments.Production });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FluentCms:Security:AllowGeneratedSigningKey"] = "true",
            ["FluentCms:Security:AllowRequestOrigin"] = "true",
            ["FluentCms:Security:AllowUnencryptedKeyRing"] = "true",
        });
        builder.Services.AddSingleton(ArticleSchemas.Titled);
        builder.Services.AddFluentCms(cms =>
        {
            cms.UseDataProvider(Provider(provider)).UseContentMigrations(contentMigrations);
            cms.Activity.Enabled = false;
            cms.DeliveryCache.OutputCache = false;
        });

        return builder.Build();
    }
}
