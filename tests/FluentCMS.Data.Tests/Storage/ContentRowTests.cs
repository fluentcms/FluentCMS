using FluentCMS.Core.Storage;
using FluentCMS.Core.Tests.Schema;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Storage;

public class ContentRowTests
{
    private const string Article = "cms_article";
    private const string ArticleLocale = "cms_article_i18n";
    private const string Tag = "cms_tag";

    private static readonly Guid Site = Guid.CreateVersion7();

    private static StorageDatabase NewDatabase() => new(SampleSchema.FromClasses().Build());

    private static Dictionary<string, object> Single(CmsDbContext context, string table, Guid documentId) =>
        context.Rows(table).Single(row => EF.Property<Guid>(row, "document_id") == documentId);

    private static Dictionary<string, object> LocaleRow(Guid documentId, string slug, short state, string locale = "en", Guid? site = null) => new()
    {
        ["document_id"] = documentId,
        ["site_id"] = site ?? Site,
        ["locale"] = locale,
        ["state"] = state,
        ["title"] = "Hello",
        ["slug"] = slug,
        ["body"] = """{"type":"doc","content":[]}""",
    };

    [Fact]
    public void SharedRowRoundTripsAndUpdates()
    {
        using var database = NewDatabase();
        var documentId = Guid.CreateVersion7();
        var publishedOn = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        const string sections = """[{"type":"hero","heading":"Welcome"}]""";

        using (var context = database.CreateCreatedContext())
        {
            context.Rows(Article).Add(new Dictionary<string, object>
            {
                ["document_id"] = documentId,
                ["site_id"] = Site,
                ["state"] = ContentState.Draft,
                ["category"] = "news",
                ["published_on"] = publishedOn,
                ["sections"] = sections,
                ["seo_meta_title"] = "Hello",
            });
            context.SaveChanges();
        }

        using (var context = database.CreateContext())
        {
            var row = Single(context, Article, documentId);
            ((Guid)row["id"]).Version.ShouldBe(7);
            row["site_id"].ShouldBe(Site);
            row["state"].ShouldBe(ContentState.Draft);
            row["category"].ShouldBe("news");
            row["published_on"].ShouldBe(publishedOn);
            row["sections"].ShouldBe(sections);
            row["seo_meta_title"].ShouldBe("Hello");

            row["category"] = "video";
            context.SaveChanges();
        }

        using var reader = database.CreateContext();
        var updated = Single(reader, Article, documentId);
        updated["category"].ShouldBe("video");
        updated["sections"].ShouldBe(sections);
    }

    [Fact]
    public void AGeneratedKeyIsStoredAsTextAndFindsTheRow()
    {
        using var database = NewDatabase();
        using var context = database.CreateCreatedContext();
        var documentId = Guid.CreateVersion7();
        context.Rows(Article).Add(new Dictionary<string, object> { ["document_id"] = documentId, ["site_id"] = Site, ["state"] = ContentState.Draft, ["category"] = "news" });
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var id = (Guid)Single(context, Article, documentId)["id"];

        context.Database.SqlQueryRaw<string>("select typeof(id) || length(id) as Value from cms_article").Single().ShouldBe("text36");
        context.ChangeTracker.Clear();
        context.Rows(Article).Count(row => EF.Property<Guid>(row, "id") == id).ShouldBe(1);
    }

    [Fact]
    public void TheTextOrderOfDocumentIdsFollowsCreationAcrossMilliseconds()
    {
        using var database = NewDatabase();
        using var context = database.CreateCreatedContext();
        var start = new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);
        var created = Enumerable.Range(0, 200).Select(i => Guid.CreateVersion7(start.AddMilliseconds(i))).ToList();

        foreach (var id in created.OrderBy(_ => Guid.NewGuid()))
        {
            context.Rows(Article).Add(new Dictionary<string, object> { ["document_id"] = id, ["site_id"] = Site, ["state"] = ContentState.Draft, ["category"] = "news" });
        }

        context.SaveChanges();

        context.Rows(Article)
            .OrderBy(row => EF.Property<Guid>(row, "document_id"))
            .Select(row => EF.Property<Guid>(row, "document_id"))
            .ToList()
            .ShouldBe(created);
    }

    [Fact]
    public void AMaxLengthIsNotEnforcedByTheDatabase()
    {
        using var database = NewDatabase();
        using var context = database.CreateCreatedContext();
        var documentId = Guid.CreateVersion7();
        var title = new string('x', 500);
        context.Rows(Article).Add(new Dictionary<string, object> { ["document_id"] = documentId, ["site_id"] = Site, ["state"] = ContentState.Draft, ["seo_meta_title"] = title });
        context.SaveChanges();
        context.ChangeTracker.Clear();

        Single(context, Article, documentId)["seo_meta_title"].ShouldBe(title);
    }

    [Fact]
    public void OmittedColumnsReadBackAsDefaults()
    {
        using var database = NewDatabase();
        var documentId = Guid.CreateVersion7();

        using (var context = database.CreateCreatedContext())
        {
            context.Rows(Article).Add(new Dictionary<string, object>
            {
                ["document_id"] = documentId,
                ["site_id"] = Site,
                ["state"] = ContentState.Draft,
            });
            context.SaveChanges();
        }

        using var reader = database.CreateContext();
        var row = Single(reader, Article, documentId);
        row["sections"].ShouldBe("[]");
        row["category"].ShouldBeNull();
        row["published_on"].ShouldBeNull();
        row["video_url"].ShouldBeNull();
    }

    [Fact]
    public void LocaleRowRoundTripsAndUpdates()
    {
        using var database = NewDatabase();
        var documentId = Guid.CreateVersion7();

        using (var context = database.CreateCreatedContext())
        {
            context.Rows(ArticleLocale).Add(LocaleRow(documentId, "hello", ContentState.Draft));
            context.SaveChanges();
        }

        using (var context = database.CreateContext())
        {
            var row = Single(context, ArticleLocale, documentId);
            row["locale"].ShouldBe("en");
            row["body"].ShouldBe("""{"type":"doc","content":[]}""");

            row["title"] = "Bonjour";
            context.SaveChanges();
        }

        using var reader = database.CreateContext();
        Single(reader, ArticleLocale, documentId)["title"].ShouldBe("Bonjour");
    }

    [Fact]
    public void RequiredColumnIsEnforcedByTheDatabase()
    {
        using var database = NewDatabase();
        using var context = database.CreateCreatedContext();
        var row = LocaleRow(Guid.CreateVersion7(), "hello", ContentState.Draft);
        row.Remove("title");
        context.Rows(ArticleLocale).Add(row);

        Should.Throw<DbUpdateException>(() => context.SaveChanges());
    }

    [Fact]
    public void OneDocumentHasOneRowPerState()
    {
        using var database = NewDatabase();
        using var context = database.CreateCreatedContext();
        var documentId = Guid.CreateVersion7();

        Dictionary<string, object> Row(short state) => new() { ["document_id"] = documentId, ["site_id"] = Site, ["state"] = state };

        context.Rows(Article).AddRange(Row(ContentState.Draft), Row(ContentState.Published));
        context.SaveChanges();
        context.ChangeTracker.Clear();

        context.Rows(Article).Add(Row(ContentState.Draft));
        Should.Throw<DbUpdateException>(() => context.SaveChanges());
    }

    [Fact]
    public void GlobalRowRoundTripsWithANullSite()
    {
        using var database = NewDatabase();
        var documentId = Guid.CreateVersion7();

        using (var context = database.CreateCreatedContext())
        {
            context.Rows(Tag).Add(new Dictionary<string, object>
            {
                ["document_id"] = documentId,
                ["state"] = ContentState.Published,
                ["name"] = "Fiction",
                ["slug"] = "fiction",
            });
            context.SaveChanges();
        }

        using var reader = database.CreateContext();
        var row = Single(reader, Tag, documentId);
        row["site_id"].ShouldBeNull();
        row["name"].ShouldBe("Fiction");
    }

    [Fact]
    public void DraftsMayShareASlugButPublishedRowsMayNot()
    {
        using var database = NewDatabase();
        using var context = database.CreateCreatedContext();

        context.Rows(ArticleLocale).AddRange(
            LocaleRow(Guid.CreateVersion7(), "same", ContentState.Draft),
            LocaleRow(Guid.CreateVersion7(), "same", ContentState.Draft),
            LocaleRow(Guid.CreateVersion7(), "same", ContentState.Published));
        context.SaveChanges();
        context.ChangeTracker.Clear();

        context.Rows(ArticleLocale).Add(LocaleRow(Guid.CreateVersion7(), "same", ContentState.Published));

        Should.Throw<DbUpdateException>(() => context.SaveChanges());
    }

    [Fact]
    public void PublishedSlugIsScopedBySiteAndLocale()
    {
        using var database = NewDatabase();
        using var context = database.CreateCreatedContext();

        context.Rows(ArticleLocale).AddRange(
            LocaleRow(Guid.CreateVersion7(), "same", ContentState.Published),
            LocaleRow(Guid.CreateVersion7(), "same", ContentState.Published, locale: "fr"),
            LocaleRow(Guid.CreateVersion7(), "same", ContentState.Published, site: Guid.CreateVersion7()));

        context.SaveChanges().ShouldBe(3);
    }

    [Fact]
    public void NonLocalizedUniqueIsScopedBySiteAndCoversOnlyPublishedRows()
    {
        var schema = SampleSchema.NewBuilder().AddCollection("page", type => type.Field("path", "text", field => field.Unique())).Build();
        using var database = new StorageDatabase(schema);
        using var context = database.CreateCreatedContext();

        static Dictionary<string, object> Row(short state, Guid site) => new()
        {
            ["document_id"] = Guid.CreateVersion7(),
            ["site_id"] = site,
            ["state"] = state,
            ["path"] = "/same",
        };

        context.Rows("cms_page").AddRange(
            Row(ContentState.Draft, Site),
            Row(ContentState.Draft, Site),
            Row(ContentState.Published, Site),
            Row(ContentState.Published, Guid.CreateVersion7()));
        context.SaveChanges().ShouldBe(4);
        context.ChangeTracker.Clear();

        context.Rows("cms_page").Add(Row(ContentState.Published, Site));

        Should.Throw<DbUpdateException>(() => context.SaveChanges());
    }

    [Fact]
    public void GlobalPublishedSlugIsUniqueDespiteTheNullSite()
    {
        using var database = NewDatabase();
        using var context = database.CreateCreatedContext();

        Dictionary<string, object> Row() => new()
        {
            ["document_id"] = Guid.CreateVersion7(),
            ["state"] = ContentState.Published,
            ["name"] = "Fiction",
            ["slug"] = "fiction",
        };

        context.Rows(Tag).Add(Row());
        context.SaveChanges();
        context.Rows(Tag).Add(Row());

        Should.Throw<DbUpdateException>(() => context.SaveChanges());
    }
}
