using FluentCMS.Core.Diagnostics;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Data.Tests.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Migrations;

public sealed class StartupReportTests : IDisposable
{
    private const string Production = "Production";

    private static readonly Dictionary<string, string?> NoMigrationOnStartup = new() { ["FluentCms:Database:MigrateOnStartup"] = "false" };

    private readonly TestDatabase _database = TestDatabase.Create();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _database.Dispose();

    [SqliteFact("schema sync, which only SQLite supports")]
    public async Task ContentMigrationsThatSchemaSyncLeavesAloneArePending()
    {
        using var host = _database.CreateHost(ArticleSchemas.Titled, Environments.Development, GeneratedMigrations.CompileFirst(ArticleSchemas.Titled));
        await host.StartAsync(Token);

        var report = host.Services.GetRequiredService<StartupReportHolder>().Current!;

        report.DataProvider.SchemaSync.ShouldBe("Auto");
        report.PendingMigrations.ShouldHaveSingleItem().Stream.ShouldBe("content");
        report.PendingMigrations.Single().Id.ShouldEndWith("_Initial");
    }

    [Fact]
    public async Task AMigratedDatabaseHasNothingPendingAndNoDrift()
    {
        using var host = _database.CreateHost(ArticleSchemas.Titled, Production, GeneratedMigrations.CompileFirst(ArticleSchemas.Titled));
        await host.StartAsync(Token);

        var report = host.Services.GetRequiredService<StartupReportHolder>().Current!;

        report.PendingMigrations.ShouldBeEmpty();
        report.Schema.Drifted.ShouldBeFalse();
        report.Schema.Hash.ShouldBe(SchemaSnapshot.Of(ArticleSchemas.Titled).Hash);
        report.Schema.AppliedHash.ShouldBe(report.Schema.Hash);
        report.DataProvider.MigrateOnStartup.ShouldBeTrue();
    }

    [Fact]
    public async Task ASchemaThatDiffersFromTheAppliedOneIsDrift()
    {
        var migrations = GeneratedMigrations.CompileFirst(ArticleSchemas.Titled);
        using (var first = _database.CreateHost(ArticleSchemas.Titled, Production, migrations))
        {
            await first.StartAsync(Token);
            await first.StopAsync(Token);
        }

        using var host = _database.CreateHost(ArticleSchemas.TitledAndRequired, Production, migrations, NoMigrationOnStartup);
        await host.StartAsync(Token);

        var schema = host.Services.GetRequiredService<StartupReportHolder>().Current!.Schema;

        schema.Drifted.ShouldBeTrue();
        schema.Hash.ShouldBe(SchemaSnapshot.Of(ArticleSchemas.TitledAndRequired).Hash);
        schema.AppliedHash.ShouldBe(SchemaSnapshot.Of(ArticleSchemas.Titled).Hash);
        schema.Changes.ShouldNotBeNull().ShouldContain("field 'article.title' changed (required)");
    }

    [Fact]
    public async Task AHistoryThatCannotBeReadIsSaidSoAndTheHostStarts()
    {
        var migrations = GeneratedMigrations.CompileFirst(ArticleSchemas.Titled);
        using (var first = _database.CreateHost(ArticleSchemas.Titled, Production, migrations))
        {
            await first.StartAsync(Token);
            await first.StopAsync(Token);
        }

        _database.Execute("drop table cms_schema_history");

        using var host = _database.CreateHost(ArticleSchemas.Titled, Production, migrations);
        await host.StartAsync(Token);

        var schema = host.Services.GetRequiredService<StartupReportHolder>().Current!.Schema;
        schema.AppliedHash.ShouldBeNull();
        schema.Changes.ShouldNotBeNull().ShouldContain("could not be read");
    }
}
