using FluentCMS.Infrastructure;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests;

public sealed class DatabaseTests
{
    [Fact]
    public async Task DbContext_opens_on_sqlite_by_default()
    {
        using var database = new TemporarySqliteDatabase();
        await using var services = BuildServices(database.Configuration);
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>();

        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", context.Database.ProviderName);

        // SQLite creates the file on first open; CanConnect only reports whether the file exists.
        await context.Database.OpenConnectionAsync();
        await context.Database.CloseConnectionAsync();

        Assert.True(await context.Database.CanConnectAsync());
    }

    [PostgresFact]
    public async Task DbContext_opens_on_postgresql_when_the_connection_string_says_so()
    {
        var connectionString = Environment.GetEnvironmentVariable(PostgresFactAttribute.VariableName)!;
        await using var services = BuildServices(new Dictionary<string, string?>
        {
            ["Database:ConnectionString"] = connectionString,
        });
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>();

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
        Assert.True(await context.Database.CanConnectAsync());
    }

    [Fact]
    public void Unknown_provider_is_rejected()
    {
        using var services = BuildServices(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "oracle",
            ["Database:ConnectionString"] = "Data Source=unused",
        });
        using var scope = services.CreateScope();

        var exception = Assert.Throws<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>());

        Assert.Contains("oracle", exception.Message);
    }

    private static ServiceProvider BuildServices(IEnumerable<KeyValuePair<string, string?>> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddFluentCmsInfrastructure(configuration);

        return services.BuildServiceProvider();
    }
}
