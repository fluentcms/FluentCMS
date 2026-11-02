using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace FluentCMS.Storage.Spike.Tests;

public sealed class ModelTests : IDisposable
{
    private readonly SpikeDatabase _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void BundledSqliteIsAtLeast345()
    {
        var version = Version.Parse(_db.ReadStrings("select sqlite_version()").Single());

        version.ShouldBeGreaterThanOrEqualTo(new Version(3, 45));
    }

    [Fact]
    public void ContentTypesAreSharedTypeEntitiesWithNoClrClass()
    {
        using var context = _db.CreateContext();

        foreach (var table in new[] { "cms_article", "cms_article_i18n", "cms_document", "cms_reference" })
        {
            var entity = context.Model.FindEntityType(table).ShouldNotBeNull();
            entity.HasSharedClrType.ShouldBeTrue();
            entity.ClrType.ShouldBe(typeof(Dictionary<string, object>));
            entity.GetTableName().ShouldBe(table);
        }
    }

    [Fact]
    public void TablesComeFromTheRuntimeSchema()
    {
        using var context = _db.CreateMigratedContext();

        var tables = _db.ReadStrings("select name from sqlite_master where type = 'table' and name not like 'sqlite_%' order by name");

        tables.ShouldBe(["cms_article", "cms_article_i18n", "cms_document", "cms_reference"]);
    }

    [Fact]
    public void EachSchemaGetsItsOwnCachedModel()
    {
        var changed = SpikeSchema.Article.WithField("article", new SpikeField("subtitle", typeof(string), Localized: true));

        using var original = _db.CreateContext();
        using var evolved = _db.CreateContext(changed);
        using var repeated = _db.CreateContext();

        original.Model.FindEntityType("cms_article_i18n")!.FindProperty("subtitle").ShouldBeNull();
        evolved.Model.FindEntityType("cms_article_i18n")!.FindProperty("subtitle").ShouldNotBeNull();
        repeated.Model.ShouldBeSameAs(original.Model);
    }

    [Fact]
    public void ARowRoundTripsThroughADictionaryAndOmittedColumnsAreNull()
    {
        var published = new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);
        Guid documentId;
        using (var writer = _db.CreateMigratedContext())
        {
            documentId = writer.AddShared(sections: "[]", publishedOn: published);
            writer.AddLocale(documentId, "Hello", "hello");
            writer.SaveChanges();
        }

        using var reader = _db.CreateContext();
        var shared = reader.Rows("cms_article").Single();
        var locale = reader.Rows("cms_article_i18n").Single();

        shared["document_id"].ShouldBe(documentId);
        shared["sections"].ShouldBe("[]");
        shared["published_on"].ShouldBe(published);
        locale["title"].ShouldBe("Hello");
        locale["body"].ShouldBeNull();
    }

    [Fact]
    public void ChangingOneValueUpdatesOnlyThatColumn()
    {
        using (var writer = _db.CreateMigratedContext())
        {
            writer.AddLocale(writer.AddShared(), "Hello", "hello");
            writer.SaveChanges();
        }

        using var context = _db.CreateContext();
        var row = context.Rows("cms_article_i18n").Single();
        row["title"] = "Changed";
        context.ChangeTracker.DetectChanges();

        var modified = context.ChangeTracker.Entries().Single().Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name);
        modified.ShouldBe(["title"]);

        context.SaveChanges();
        _db.ReadStrings("select title from cms_article_i18n").ShouldBe(["Changed"]);
    }
}
