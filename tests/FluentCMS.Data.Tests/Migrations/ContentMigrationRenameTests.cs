using System.Reflection;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Migrations;

// A content migration generated for fields renamed with [FormerNames], applied on the provider of the run to rows written
// under the former names.
public sealed class ContentMigrationRenameTests : IDisposable
{
    private const string EmptyDocument = """{"type":"doc","content":[]}""";

    private readonly TestDatabase _database = TestDatabase.Create();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task AScalarAndAJsonFieldKeepTheirValuesAndTheirIndexesAndChecksTakeTheNewName()
    {
        var before = SampleSchema.NewBuilder()
            .AddCollection("article", type => type
                .Field("title", "text")
                .Field("subtitle", "text", field => field.Indexed().Unique())
                .Field("content", "richText"))
            .Build();
        var after = SampleSchema.NewBuilder()
            .AddCollection("article", type => type
                .Field("title", "text")
                .Field("summary", "text", field => field.Indexed().Unique().FormerNames("subtitle"))
                .Field("body", "richText", field => field.FormerNames("content")))
            .Build();

        var migration = await RenameAsync(before, after, $"insert into cms_article (id, document_id, site_id, state, title, subtitle, content) values ({Row(published: true)}, 'Title', 'Kept', '{EmptyDocument}')");

        migration.Up.OfType<RenameColumnOperation>().Select(rename => (rename.Name, rename.NewName)).ShouldBe([("content", "body"), ("subtitle", "summary")], ignoreOrder: true);
        migration.Up.ShouldNotContain(operation => operation is DropColumnOperation || operation is AddColumnOperation);
        migration.Up.OfType<RenameIndexOperation>().ShouldContain(rename => rename.Name == "ix_cms_article_subtitle" && rename.NewName == "ix_cms_article_summary");
        migration.Down.OfType<RenameColumnOperation>().Select(rename => (rename.Name, rename.NewName)).ShouldBe([("body", "content"), ("summary", "subtitle")], ignoreOrder: true);

        _database.ReadStrings("select summary from cms_article").ShouldBe(["Kept"]);
        _database.ReadStrings("select body from cms_article").ShouldHaveSingleItem().ShouldContain("doc");

        var table = await TableAsync(after, "cms_article");
        table.Indexes.Select(index => index.Name).ShouldContain("ix_cms_article_summary");
        table.Indexes.Select(index => index.Name).ShouldContain("ux_cms_article_summary");
        table.Indexes.ShouldNotContain(index => index.Name.Contains("subtitle", StringComparison.Ordinal));
        if (TestProvider.IsSqlServer)
        {
            table.CheckConstraints.Select(check => check.Name).ShouldBe(["ck_cms_article_body_json"]);
        }
    }

    [Fact]
    public async Task ALocalizedFieldKeepsItsValuesInTheLocaleTable()
    {
        var before = SampleSchema.NewBuilder()
            .AddCollection("article", type => type.Localized().Field("title", "text", field => field.Localizable()).Field("caption", "text", field => field.Localizable().Indexed()))
            .Build();
        var after = SampleSchema.NewBuilder()
            .AddCollection("article", type => type.Localized().Field("title", "text", field => field.Localizable()).Field("heading", "text", field => field.Localizable().Indexed().FormerNames("caption")))
            .Build();

        var migration = await RenameAsync(before, after, $"insert into cms_article_i18n (id, document_id, site_id, state, locale, title, caption) values ({Row()}, 'en', 'Title', 'Kept')");

        migration.Up.OfType<RenameColumnOperation>().ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            rename => rename.Table.ShouldBe("cms_article_i18n"),
            rename => rename.Name.ShouldBe("caption"),
            rename => rename.NewName.ShouldBe("heading"));
        _database.ReadStrings("select heading from cms_article_i18n").ShouldBe(["Kept"]);
        (await TableAsync(after, "cms_article_i18n")).Indexes.Select(index => index.Name).ShouldContain("ix_cms_article_i18n_heading");
    }

    [Fact]
    public async Task TheColumnsOfAFlattenedComponentTakeTheNamesOfTheFieldAndOfTheComponentField()
    {
        var before = SampleSchema.NewBuilder()
            .AddComponent("seo", component => component.Field("metaTitle", "text").Field("noIndex", "boolean"))
            .AddCollection("article", type => type.Field("title", "text").Field("meta", "component", field => field.Setting(FieldSettings.Component, "seo")))
            .Build();
        var after = SampleSchema.NewBuilder()
            .AddComponent("seo", component => component.Field("pageTitle", "text", field => field.FormerNames("metaTitle")).Field("noIndex", "boolean"))
            .AddCollection("article", type => type.Field("title", "text").Field("seo", "component", field => field.Setting(FieldSettings.Component, "seo").FormerNames("meta")))
            .Build();

        var migration = await RenameAsync(before, after, $"insert into cms_article (id, document_id, site_id, state, title, meta_meta_title, meta_no_index) values ({Row()}, 'Title', 'Kept', {(TestProvider.IsPostgreSql ? "true" : "1")})");

        migration.Up.OfType<RenameColumnOperation>().Select(rename => (rename.Name, rename.NewName)).ShouldBe([("meta_meta_title", "seo_page_title"), ("meta_no_index", "seo_no_index")], ignoreOrder: true);
        _database.ReadStrings("select seo_page_title from cms_article").ShouldBe(["Kept"]);
        _database.Columns("cms_article").ShouldNotContain(column => column.StartsWith("meta_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheReferencesOfARenamedRelationMoveToItsPath()
    {
        var before = SampleSchema.NewBuilder()
            .AddCollection("author", type => type.Field("name", "text"))
            .AddComponent("card", component => component.Field("owner", "relation", field => field.Relation("author")))
            .AddCollection("article", type => type
                .Field("title", "text")
                .Field("writer", "relation", field => field.Relation("author"))
                .Field("meta", "component", field => field.Setting(FieldSettings.Component, "card")))
            .Build();
        var after = SampleSchema.NewBuilder()
            .AddCollection("author", type => type.Field("name", "text"))
            .AddComponent("card", component => component.Field("person", "relation", field => field.Relation("author").FormerNames("owner")))
            .AddCollection("article", type => type
                .Field("title", "text")
                .Field("author", "relation", field => field.Relation("author").FormerNames("writer"))
                .Field("card", "component", field => field.Setting(FieldSettings.Component, "card").FormerNames("meta")))
            .Build();

        var migration = await RenameAsync(before, after, string.Join(';', Reference("article", "writer"), Reference("article", "meta.owner"), Reference("author", "writer")));

        migration.Up.ShouldNotContain(operation => operation is ColumnOperation || operation is RenameColumnOperation);
        migration.Up.OfType<UpdateDataOperation>().Count().ShouldBe(2);
        migration.Down.OfType<UpdateDataOperation>().Count().ShouldBe(2);
        _database.ReadStrings("select field from cms_reference where source_type = 'article'").ShouldBe(["author", "card.person"], ignoreOrder: true);
        _database.ReadStrings("select field from cms_reference where source_type = 'author'").ShouldBe(["writer"]);
    }

    // Applies the first migration of the schema before, writes the rows and then applies the migration to the schema after.
    private async Task<(IReadOnlyList<MigrationOperation> Up, IReadOnlyList<MigrationOperation> Down)> RenameAsync(SchemaModel before, SchemaModel after, string rows)
    {
        var first = GeneratedMigrations.Scaffold(before, "Initial");
        var initial = GeneratedMigrations.Compile(first.MigrationCode, first.MetadataCode, first.SnapshotCode);
        await StartAsync(before, initial);

        foreach (var row in rows.Split(';'))
        {
            _database.Execute(row);
        }

        var second = GeneratedMigrations.Scaffold(after, "Rename", initial);
        var migrations = GeneratedMigrations.Compile(first.MigrationCode, first.MetadataCode, second.MigrationCode, second.MetadataCode, second.SnapshotCode);
        await StartAsync(after, migrations);

        var rename = (Migration)Activator.CreateInstance(migrations.GetTypes().Single(type => type.IsSubclassOf(typeof(Migration)) && type.Name == "Rename"))!;
        return (rename.UpOperations, rename.DownOperations);
    }

    private async Task StartAsync(SchemaModel schema, Assembly migrations)
    {
        using var host = _database.CreateHost(schema, "Production", migrations);
        await host.StartAsync(Token);
        await host.StopAsync(Token);
    }

    private async Task<DatabaseTable> TableAsync(SchemaModel schema, string table)
    {
        using var services = _database.CreateServices(schema);
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        await context.Database.OpenConnectionAsync(Token);
        var database = await scope.ServiceProvider.GetRequiredService<IDataProvider>().SchemaReader.ReadAsync(context.Database.GetDbConnection(), Token);
        return database.Tables[table];
    }

    private static string Row(bool published = false) => $"'{Guid.CreateVersion7()}', '{Guid.CreateVersion7()}', '{Guid.Empty}', {(published ? 1 : 0)}";

    private static string Reference(string sourceType, string field) =>
        $"insert into cms_reference (id, source_type, source_document_id, state, locale, field, position, target_type, target_id) values ('{Guid.CreateVersion7()}', '{sourceType}', '{Guid.CreateVersion7()}', 0, '', '{field}', 0, 'author', '{Guid.CreateVersion7()}')";
}
