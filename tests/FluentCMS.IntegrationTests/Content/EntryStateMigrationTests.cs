using FluentCMS.Core.Content;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Content;

public sealed class EntryStateMigrationTests
{
    // Entries published before states existed come out of the migration published, and the rest as drafts (CNT-01).
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Entries_published_before_states_existed_become_published(string provider)
    {
        await using var database = await TemporaryDatabase.CreateAsync(provider);
        await using var services = TestServices.Build(database.Configuration);
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>();
        var migrations = context.Database.GetMigrations().ToList();
        var stateMigration = migrations.Single(migration => migration.EndsWith("_EntryState", StringComparison.Ordinal));
        await context.Database.MigrateAsync(migrations[migrations.IndexOf(stateMigration) - 1]);

        var (type, published, draft) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var site = Site.Default.Id;
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""INSERT INTO "ContentTypes" ("Id", "SiteId", "Name", "Slug", "Version") VALUES ({type}, {site}, 'Page', 'page', 1)""");
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""INSERT INTO "Entries" ("Id", "SiteId", "ContentTypeId", "LatestVersion", "PublishedVersion") VALUES ({published}, {site}, {type}, 2, 1)""");
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""INSERT INTO "Entries" ("Id", "SiteId", "ContentTypeId", "LatestVersion", "PublishedVersion") VALUES ({draft}, {site}, {type}, 1, NULL)""");

        await context.Database.MigrateAsync();

        var states = await context.Entries.AsNoTracking().ToDictionaryAsync(row => row.Id, row => row.State);
        Assert.Equal(EntryState.Published, states[published]);
        Assert.Equal(EntryState.Draft, states[draft]);
    }
}
