using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace FluentCMS.Storage.Spike.Tests;

public sealed class PartialUniqueIndexTests : IDisposable
{
    private readonly SpikeDatabase _db = new();
    private readonly SpikeDbContext _context;

    public PartialUniqueIndexTests() => _context = _db.CreateMigratedContext();

    public void Dispose()
    {
        _context.Dispose();
        _db.Dispose();
    }

    [Fact]
    public void OneRowPerDocumentStateAndLocaleIsEnforced()
    {
        var documentId = Guid.CreateVersion7();
        _context.AddShared(documentId);
        _context.AddLocale(documentId, "One", "one");
        _context.SaveChanges();

        _context.AddShared(documentId);
        Should.Throw<DbUpdateException>(() => _context.SaveChanges());
        _context.ChangeTracker.Clear();

        _context.AddLocale(documentId, "Two", "two");
        Should.Throw<DbUpdateException>(() => _context.SaveChanges());
    }

    [Fact]
    public void IndexIsCreatedWithTheStateFilter()
    {
        var sql = _db.ReadStrings("select sql from sqlite_master where name = 'ux_cms_article_i18n_slug'").Single();

        sql.ShouldBe("""CREATE UNIQUE INDEX "ux_cms_article_i18n_slug" ON "cms_article_i18n" ("site_id", "locale", "slug") WHERE state = 1""");
    }

    [Fact]
    public void ASecondPublishedRowWithTheSameSlugIsRejected()
    {
        _context.AddLocale(Guid.CreateVersion7(), "One", "same");
        _context.SaveChanges();

        _context.AddLocale(Guid.CreateVersion7(), "Two", "same");
        var exception = Should.Throw<DbUpdateException>(() => _context.SaveChanges());

        var sqlite = exception.InnerException.ShouldBeOfType<SqliteException>();
        sqlite.SqliteErrorCode.ShouldBe(19);
        sqlite.SqliteExtendedErrorCode.ShouldBe(2067);
    }

    [Fact]
    public void DraftRowsMayShareASlug()
    {
        _context.AddLocale(Guid.CreateVersion7(), "One", "same", state: 0);
        _context.AddLocale(Guid.CreateVersion7(), "Two", "same", state: 0);
        _context.AddLocale(Guid.CreateVersion7(), "Three", "same", state: 1);

        _context.SaveChanges().ShouldBe(3);
    }

    [Fact]
    public void TheSameSlugIsAllowedInAnotherLocaleOrSite()
    {
        _context.AddLocale(Guid.CreateVersion7(), "One", "same", locale: "en", siteId: SpikeSeed.SiteA);
        _context.AddLocale(Guid.CreateVersion7(), "Two", "same", locale: "fr", siteId: SpikeSeed.SiteA);
        _context.AddLocale(Guid.CreateVersion7(), "Three", "same", locale: "en", siteId: SpikeSeed.SiteB);

        _context.SaveChanges().ShouldBe(3);
    }

    [Fact]
    public void QueryPlanMatchesThePartialIndexForALiteralOrAMatchingBoundState()
    {
        var published = 1;
        var draft = 0;
        var site = SpikeSeed.SiteA;
        var rows = _context.Rows("cms_article_i18n");

        var literal = rows.Where(r => EF.Property<Guid>(r, "site_id") == site
            && EF.Property<string>(r, "locale") == "en"
            && EF.Property<string>(r, "slug") == "hello"
            && EF.Property<int>(r, "state") == 1);
        var inlined = rows.Where(r => EF.Property<Guid>(r, "site_id") == site
            && EF.Property<string>(r, "locale") == "en"
            && EF.Property<string>(r, "slug") == "hello"
            && EF.Property<int>(r, "state") == EF.Constant(published));
        var boundPublished = rows.Where(r => EF.Property<Guid>(r, "site_id") == site
            && EF.Property<string>(r, "locale") == "en"
            && EF.Property<string>(r, "slug") == "hello"
            && EF.Property<int>(r, "state") == published);
        var boundDraft = rows.Where(r => EF.Property<Guid>(r, "site_id") == site
            && EF.Property<string>(r, "locale") == "en"
            && EF.Property<string>(r, "slug") == "hello"
            && EF.Property<int>(r, "state") == draft);

        SpikeDatabase.QueryPlan(literal).ShouldContain("ux_cms_article_i18n_slug");
        SpikeDatabase.QueryPlan(inlined).ShouldContain("ux_cms_article_i18n_slug");
        SpikeDatabase.QueryPlan(boundPublished).ShouldContain("ux_cms_article_i18n_slug");
        SpikeDatabase.QueryPlan(boundDraft).ShouldNotContain("ux_cms_article_i18n_slug");
    }

    [Fact]
    public void NonLocalizedUniqueFieldIsScopedToTheSiteOnly()
    {
        using var db = new SpikeDatabase();
        using var context = db.CreateContext(SpikeSchema.Article.WithField("article", new SpikeField("external_key", typeof(string), Unique: true)));
        context.Database.EnsureCreated();

        db.ReadStrings("select sql from sqlite_master where name = 'ux_cms_article_external_key'").Single()
            .ShouldBe("""CREATE UNIQUE INDEX "ux_cms_article_external_key" ON "cms_article" ("site_id", "external_key") WHERE state = 1""");

        AddSharedRow(context, SpikeSeed.SiteA, "same");
        context.SaveChanges();
        AddSharedRow(context, SpikeSeed.SiteB, "same");
        context.SaveChanges();

        AddSharedRow(context, SpikeSeed.SiteA, "same");
        Should.Throw<DbUpdateException>(() => context.SaveChanges());
    }

    [Fact]
    public void GlobalTypeStoresANullSiteAndLeavesTheSiteOutOfItsUniqueIndex()
    {
        using var db = new SpikeDatabase();
        using var context = db.CreateContext(new SpikeSchema([SpikeSchema.Article.Types[0] with { Global = true }]));
        context.Database.EnsureCreated();

        db.ReadStrings("select sql from sqlite_master where name = 'ux_cms_article_i18n_slug'").Single()
            .ShouldBe("""CREATE UNIQUE INDEX "ux_cms_article_i18n_slug" ON "cms_article_i18n" ("locale", "slug") WHERE state = 1""");

        AddGlobalLocaleRow(context, "same");
        context.SaveChanges();
        db.ReadStrings("select typeof(site_id) from cms_article_i18n").ShouldBe(["null"]);

        AddGlobalLocaleRow(context, "same");
        Should.Throw<DbUpdateException>(() => context.SaveChanges());
    }

    private static void AddSharedRow(SpikeDbContext context, Guid site, string externalKey) =>
        context.Rows("cms_article").Add(new Dictionary<string, object>
        {
            ["document_id"] = Guid.CreateVersion7(),
            ["state"] = 1,
            ["site_id"] = site,
            ["category"] = "news",
            ["external_key"] = externalKey,
        });

    private static void AddGlobalLocaleRow(SpikeDbContext context, string slug) =>
        context.Rows("cms_article_i18n").Add(new Dictionary<string, object>
        {
            ["document_id"] = Guid.CreateVersion7(),
            ["locale"] = "en",
            ["state"] = 1,
            ["title"] = "Global",
            ["slug"] = slug,
        });

    [Fact]
    public void ReferenceKeyEnforcesUniquenessWithAnEmptyLocale()
    {
        var source = Guid.CreateVersion7();
        var target = Guid.CreateVersion7();
        _context.AddReference(source, "author", target);
        _context.SaveChanges();

        _context.AddReference(source, "author", target);

        Should.Throw<DbUpdateException>(() => _context.SaveChanges());
    }

    [Fact]
    public void NullsAreDistinctInAUniqueIndexSoANullLocaleOrSiteDefeatsIt()
    {
        _db.Execute("create table demo (site_id text, locale text, slug text); create unique index ux_demo on demo (site_id, locale, slug)");

        _db.Execute("insert into demo values (null, 'en', 'a')");
        _db.Execute("insert into demo values (null, 'en', 'a')");
        _db.Execute("insert into demo values ('s', null, 'a')");
        _db.Execute("insert into demo values ('s', null, 'a')");

        _db.ReadStrings("select slug from demo").Count.ShouldBe(4);
    }
}
