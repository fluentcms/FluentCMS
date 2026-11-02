using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Publishing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Sqlite;

public sealed class SystemMigrationTests : IDisposable
{
    private readonly SqliteFileDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private static List<string> Tables(DbContext context)
    {
        context.Database.OpenConnection();
        using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "select name from sqlite_master where type = 'table' and name not like 'sqlite_%' order by name";
        using var reader = command.ExecuteReader();

        var tables = new List<string>();
        while (reader.Read())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private static long Count(DbContext context, string table)
    {
        context.Database.OpenConnection();
        using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"select count(*) from {table}";
        return (long)command.ExecuteScalar()!;
    }

    [Fact]
    public void AppliesToAFreshFileDatabaseWithItsOwnHistoryTable()
    {
        using var services = _database.CreateServices(SampleSchema.FromClasses().Build());
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();

        context.Database.GetPendingMigrations().Count().ShouldBe(4);
        context.Database.Migrate();

        Tables(context).ShouldBe(["__EFMigrationsLock", "cms__migrations", "cms_document", "cms_reference", "cms_role", "cms_role_permission", "cms_site", "cms_site_locale", "cms_version"], ignoreOrder: true);
        Count(context, SystemDbContext.HistoryTable).ShouldBe(4);
        context.Database.GetPendingMigrations().ShouldBeEmpty();
    }

    [Fact]
    public void TheModelHasNoPendingChanges()
    {
        using var services = _database.CreateServices();
        using var scope = services.CreateScope();

        scope.ServiceProvider.GetRequiredService<SystemDbContext>().Database.HasPendingModelChanges().ShouldBeFalse();
    }

    [Fact]
    public void TheModelHoldsOnlySystemTablesWhateverTheSchema()
    {
        using var services = _database.CreateServices(SampleSchema.FromClasses().Build());
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();

        context.Model.GetEntityTypes().Select(type => type.GetTableName()).ShouldBe(["cms_document", "cms_reference", "cms_role", "cms_role_permission", "cms_site", "cms_site_locale", "cms_version"], ignoreOrder: true);
    }

    [Fact]
    public void CmsDbContextReadsAndWritesTheMigratedTables()
    {
        using var services = _database.CreateServices();
        using (var scope = services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<SystemDbContext>().Database.Migrate();
        }

        var at = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        using (var scope = services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            context.Documents.Add(new Document { DocumentId = Guid.CreateVersion7(), Type = "post", Status = EntryStatus.Draft, CreatedAt = at, UpdatedAt = at });
            context.Versions.Add(new ContentVersion { DocumentId = Guid.CreateVersion7(), Number = 1, Snapshot = "{}", CreatedAt = at });
            context.SaveChanges();
        }

        using (var scope = services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<CmsDbContext>().Documents.Single().CreatedAt.ShouldBe(at);
        }
    }
}
