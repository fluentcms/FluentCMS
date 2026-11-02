using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Shouldly;
using Xunit;

namespace FluentCMS.Storage.Spike.Tests;

public sealed class MigrationTests : IDisposable
{
    private const string ContentHistory = "cms_content__migrations";
    private const string SystemHistory = "cms__migrations";

    private readonly SpikeDatabase _db = new();

    public void Dispose() => _db.Dispose();

    private static string[] AllFiles(ScaffoldedMigration migration) =>
        [migration.MigrationCode, migration.MetadataCode, migration.SnapshotCode];

    private List<string> Tables() =>
        _db.ReadStrings("select name from sqlite_master where type = 'table' and name not like 'sqlite_%' order by name");

    [Fact]
    public void InitialMigrationIsScaffoldedFromThePropertyBagModel()
    {
        using var context = _db.CreateContext(stream: SpikeTables.Content);

        var migration = MigrationTool.Scaffold(context, "Initial");

        migration.MigrationCode.ShouldContain("name: \"cms_article\"");
        migration.MigrationCode.ShouldContain("name: \"cms_article_i18n\"");
        migration.MigrationCode.ShouldContain("name: \"ux_cms_article_i18n_slug\"");
        migration.MigrationCode.ShouldContain("filter: \"state = 1\"");
        migration.MigrationCode.ShouldContain("json_valid(sections)");
        migration.SnapshotCode.ShouldContain("\"cms_article_i18n\"");
    }

    [Fact]
    public void ContentStreamLeavesSystemTablesOutOfItsMigration()
    {
        using var context = _db.CreateContext(stream: SpikeTables.Content);

        var migration = MigrationTool.Scaffold(context, "Initial");

        migration.MigrationCode.ShouldNotContain("cms_document");
        migration.MigrationCode.ShouldNotContain("cms_reference");
        migration.MigrationCode.ShouldNotContain("json_each");
    }

    [Fact]
    public void ScaffoldedMigrationCompilesAppliesAndMatchesTheModel()
    {
        Assembly assembly;
        using (var context = _db.CreateContext(stream: SpikeTables.Content, historyTable: ContentHistory))
        {
            assembly = MigrationTool.Compile(AllFiles(MigrationTool.Scaffold(context, "Initial")));
        }

        using var migrating = _db.CreateContext(stream: SpikeTables.Content, migrations: assembly, historyTable: ContentHistory);
        migrating.Database.HasPendingModelChanges().ShouldBeFalse();
        migrating.GetService<IMigrator>().Migrate();

        Tables().ShouldContain("cms_article");
        Tables().ShouldContain("cms_article_i18n");
        Tables().ShouldContain(ContentHistory);
        Tables().ShouldNotContain("cms_document");
        _db.ReadStrings($"select count(*) || '' from {ContentHistory}").ShouldBe(["1"]);
        _db.ReadStrings("select sql from sqlite_master where name = 'ux_cms_article_i18n_slug'").Single().ShouldEndWith("WHERE state = 1");
    }

    [Fact]
    public void AChangedSchemaScaffoldsAnAddColumnAndKeepsExistingRows()
    {
        var evolvedSchema = SpikeSchema.Article.WithField("article", new SpikeField("subtitle", typeof(string), Localized: true, MaxLength: 200));

        ScaffoldedMigration first;
        Assembly firstAssembly;
        using (var context = _db.CreateContext(stream: SpikeTables.Content, historyTable: ContentHistory))
        {
            first = MigrationTool.Scaffold(context, "Initial");
            firstAssembly = MigrationTool.Compile(AllFiles(first));
        }

        var documentId = Guid.CreateVersion7();
        using (var context = _db.CreateContext(stream: SpikeTables.Content, migrations: firstAssembly, historyTable: ContentHistory))
        {
            context.GetService<IMigrator>().Migrate();
            context.AddLocale(documentId, "Kept", "kept");
            context.SaveChanges();
        }

        using (var context = _db.CreateContext(evolvedSchema, SpikeTables.Content, firstAssembly, ContentHistory))
        {
            context.Database.HasPendingModelChanges().ShouldBeTrue();
        }

        ScaffoldedMigration second;
        using (var context = _db.CreateContext(evolvedSchema, SpikeTables.Content, firstAssembly, ContentHistory))
        {
            second = MigrationTool.Scaffold(context, "AddSubtitle");
        }

        second.MigrationCode.ShouldContain("migrationBuilder.AddColumn<string>");
        second.MigrationCode.ShouldContain("name: \"subtitle\"");
        second.MigrationCode.ShouldNotContain("DropTable");

        var secondAssembly = MigrationTool.Compile(first.MigrationCode, first.MetadataCode, second.MigrationCode, second.MetadataCode, second.SnapshotCode);
        using var evolved = _db.CreateContext(evolvedSchema, SpikeTables.Content, secondAssembly, ContentHistory);
        evolved.GetService<IMigrator>().Migrate();

        evolved.Database.HasPendingModelChanges().ShouldBeFalse();
        var row = evolved.Rows("cms_article_i18n").Single();
        row["title"].ShouldBe("Kept");
        row["subtitle"].ShouldBeNull();
    }

    // A trap for `fluentcms migrations add` and `migrate --check`: the wrong migration is produced silently.
    [Fact]
    public void ScaffoldingOnAContextThatCheckedForPendingChangesRecreatesEveryTable()
    {
        Assembly assembly;
        using (var context = _db.CreateContext(stream: SpikeTables.Content, historyTable: ContentHistory))
        {
            assembly = MigrationTool.Compile(AllFiles(MigrationTool.Scaffold(context, "Initial")));
        }

        using var reused = _db.CreateContext(stream: SpikeTables.Content, migrations: assembly, historyTable: ContentHistory);
        reused.Database.HasPendingModelChanges().ShouldBeFalse();
        MigrationTool.Scaffold(reused, "Reused").MigrationCode.ShouldContain("CreateTable");

        using var fresh = _db.CreateContext(stream: SpikeTables.Content, migrations: assembly, historyTable: ContentHistory);
        MigrationTool.Scaffold(fresh, "Fresh").MigrationCode.ShouldNotContain("CreateTable");
    }

    [Fact]
    public void SystemAndContentStreamsShareOneDatabaseWithTheirOwnHistoryTables()
    {
        Assembly systemAssembly;
        using (var context = _db.CreateContext(stream: SpikeTables.System, historyTable: SystemHistory))
        {
            systemAssembly = MigrationTool.Compile(AllFiles(MigrationTool.Scaffold(context, "System")));
        }

        Assembly contentAssembly;
        using (var context = _db.CreateContext(stream: SpikeTables.Content, historyTable: ContentHistory))
        {
            contentAssembly = MigrationTool.Compile(AllFiles(MigrationTool.Scaffold(context, "Content")));
        }

        using (var context = _db.CreateContext(stream: SpikeTables.System, migrations: systemAssembly, historyTable: SystemHistory))
        {
            context.GetService<IMigrator>().Migrate();
        }

        using (var context = _db.CreateContext(stream: SpikeTables.Content, migrations: contentAssembly, historyTable: ContentHistory))
        {
            context.GetService<IMigrator>().Migrate();
        }

        Tables().ShouldBe(
            ["__EFMigrationsLock", "cms__migrations", "cms_article", "cms_article_i18n", "cms_content__migrations", "cms_document", "cms_reference"],
            ignoreOrder: true);

        using var union = _db.CreateContext();
        union.AddDocument(Guid.CreateVersion7());
        union.AddShared();
        union.SaveChanges().ShouldBe(2);
    }

    [Fact]
    public void MigratorScriptContainsThePartialIndex()
    {
        Assembly assembly;
        using (var context = _db.CreateContext(stream: SpikeTables.Content, historyTable: ContentHistory))
        {
            assembly = MigrationTool.Compile(AllFiles(MigrationTool.Scaffold(context, "Initial")));
        }

        using var scripting = _db.CreateContext(stream: SpikeTables.Content, migrations: assembly, historyTable: ContentHistory);
        var script = scripting.GetService<IMigrator>().GenerateScript();

        script.ShouldContain("""CREATE UNIQUE INDEX "ux_cms_article_i18n_slug" ON "cms_article_i18n" ("site_id", "locale", "slug") WHERE state = 1;""");
    }
}
