using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Data.Sqlite;
using FluentCMS.Publishing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Sqlite;

public class HostTests
{
    [Fact]
    public void AHostWithNoProviderFailsWithAClearError()
    {
        var exception = Should.Throw<FluentCmsConfigurationException>(() => new ServiceCollection().AddFluentCms(_ => { }));

        exception.Code.ShouldBe("FCMS1302");
        exception.Message.ShouldStartWith("FCMS1302: ");
        exception.Message.ShouldContain("cms.UseSqlite(...)");
    }

    [Fact]
    public void AHostWithTwoProvidersFailsWithAClearError()
    {
        var exception = Should.Throw<FluentCmsConfigurationException>(() => new ServiceCollection().AddFluentCms(cms => cms
            .UseSqlite("Data Source=a.db")
            .UseSqlite("Data Source=b.db")));

        exception.Code.ShouldBe("FCMS1303");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AMissingConnectionStringIsRejected(string? connectionString) =>
        Should.Throw<ArgumentException>(() => new ServiceCollection().AddFluentCms(cms => cms.UseSqlite(connectionString)));

    [Fact]
    public void AHostWithUseSqliteResolvesAWorkingContext()
    {
        using var database = new SqliteFileDatabase();
        using var services = database.CreateServices();
        var at = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

        using (var scope = services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            context.Database.EnsureCreated();
            context.Documents.Add(new Document { DocumentId = Guid.CreateVersion7(), Type = "post", Status = EntryStatus.Draft, CreatedAt = at, UpdatedAt = at });
            context.SaveChanges();
        }

        using (var scope = services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<CmsDbContext>().Documents.Single().Type.ShouldBe("post");
        }
    }

    [Fact]
    public void AHostSchemaReachesTheContext()
    {
        using var database = new SqliteFileDatabase();
        using var services = database.CreateServices(SampleSchema.FromClasses().Build());
        using var scope = services.CreateScope();

        scope.ServiceProvider.GetRequiredService<CmsDbContext>().Model.FindEntityType("cms_article").ShouldNotBeNull();
    }

    [Fact]
    public void TheProviderRegistersItsPartsAndDefaults()
    {
        using var database = new SqliteFileDatabase();
        using var services = database.CreateServices();

        var provider = services.GetRequiredService<IDataProvider>();
        provider.ShouldBeOfType<SqliteDataProvider>();
        provider.MigrateOnStartup.ShouldBeTrue();
        services.GetRequiredService<IStorageConventions>().ShouldBeOfType<SqliteStorageConventions>();
        services.GetRequiredService<IJsonSqlDialect>().ShouldBeOfType<SqliteJsonDialect>();
        services.GetRequiredService<IJobClaimDialect>().ShouldBeOfType<SqliteJobClaimDialect>();
    }
}
