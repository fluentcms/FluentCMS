using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests;

public sealed class DatabaseTests
{
    [Fact]
    public async Task Migrations_apply_on_sqlite_by_default()
    {
        using var database = new TemporarySqliteDatabase();
        await using var services = TestServices.Build(database.Configuration);

        await TestServices.MigrateAsync(services);

        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>();

        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", context.Database.ProviderName);
        Assert.NotEmpty(await context.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.False(await context.Users.AnyAsync());
        Assert.False(await context.AuditEvents.AnyAsync());
    }

    // Upgrades must be reversible one version back (OPS-04, ADR-0005), so the newest migration's Down is exercised here.
    [Fact]
    public async Task The_newest_migration_reverts_and_reapplies_on_sqlite()
    {
        using var database = new TemporarySqliteDatabase();
        await using var services = TestServices.Build(database.Configuration);
        await TestServices.MigrateAsync(services);

        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>();
        var migrations = context.Database.GetMigrations().ToList();
        Assert.True(migrations.Count >= 2);

        await context.Database.MigrateAsync(migrations[^2]);

        Assert.Equal(migrations[..^1], await context.Database.GetAppliedMigrationsAsync());
        Assert.Equal([migrations[^1]], await context.Database.GetPendingMigrationsAsync());

        await context.Database.MigrateAsync();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.False(await context.AuditEvents.AnyAsync());
    }

    [PostgresFact]
    public async Task Migrations_apply_on_postgresql_when_the_connection_string_says_so()
    {
        var connectionString = Environment.GetEnvironmentVariable(PostgresFactAttribute.VariableName)!;
        await using var services = TestServices.Build(new Dictionary<string, string?>
        {
            ["Database:ConnectionString"] = connectionString,
        });

        await TestServices.MigrateAsync(services);

        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>();

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
        Assert.NotEmpty(await context.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.False(await context.Users.AnyAsync());
        Assert.False(await context.AuditEvents.AnyAsync());
    }

    [Fact]
    public void Unknown_provider_is_rejected_at_startup()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => TestServices.Build(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "oracle",
            ["Database:ConnectionString"] = "Data Source=unused",
        }));

        Assert.Contains("oracle", exception.Message);
    }
}
