using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace FluentCMS.Storage.Spike.Tests;

public sealed class SqliteTypeLimitTests : IDisposable
{
    private readonly SpikeDatabase _db = new();
    private readonly SpikeDbContext _context;

    public SqliteTypeLimitTests() => _context = _db.CreateMigratedContext();

    public void Dispose()
    {
        _context.Dispose();
        _db.Dispose();
    }

    [Fact]
    public void PlainDateTimeOffsetCannotBeComparedOrSorted()
    {
        var cutoff = DateTimeOffset.UtcNow;
        var documents = _context.Rows(SpikeDbContext.Document);

        Should.Throw<InvalidOperationException>(() =>
            documents.Where(r => EF.Property<DateTimeOffset>(r, "created_at") > cutoff).ToList());
        Should.Throw<NotSupportedException>(() =>
            documents.OrderBy(r => EF.Property<DateTimeOffset>(r, "created_at")).ToList());

        documents.Where(r => EF.Property<DateTimeOffset>(r, "created_at") == cutoff).ToList().ShouldBeEmpty();
    }

    [Fact]
    public void UtcTicksConverterMakesDateTimeOffsetComparableAndSortableAcrossOffsets()
    {
        var earlier = _context.AddShared(publishedOn: new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.FromHours(5)));
        var later = _context.AddShared(publishedOn: new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero));
        _context.SaveChanges();

        DateTimeOffset? cutoff = new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);
        var articles = _context.Rows("cms_article");

        articles.OrderBy(r => EF.Property<DateTimeOffset?>(r, "published_on"))
            .Select(r => EF.Property<Guid>(r, "document_id"))
            .ToList()
            .ShouldBe([earlier, later]);
        articles.Where(r => EF.Property<DateTimeOffset?>(r, "published_on") > cutoff)
            .Select(r => EF.Property<Guid>(r, "document_id"))
            .ToList()
            .ShouldBe([later]);
        _db.ReadStrings("select distinct typeof(published_on) from cms_article").ShouldBe(["integer"]);
    }

    [Fact]
    public void UtcTicksConventionCoversSystemColumnsThatSchedulingAndListsNeed()
    {
        using var db = new SpikeDatabase();
        using var context = db.CreateContext(SpikeSchema.Article with { UtcTicksDates = true });
        context.Database.EnsureCreated();

        var due = Guid.CreateVersion7();
        var notDue = Guid.CreateVersion7();
        var now = new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);
        context.AddDocument(due);
        context.AddDocument(notDue);
        context.SaveChanges();
        db.Execute($"update cms_document set schedule_publish_at = {now.AddHours(-1).UtcTicks} where document_id = '{due.ToString().ToUpperInvariant()}'");
        db.Execute($"update cms_document set schedule_publish_at = {now.AddHours(1).UtcTicks} where document_id = '{notDue.ToString().ToUpperInvariant()}'");

        DateTimeOffset? cutoff = now;
        var documents = context.Rows(SpikeDbContext.Document);

        documents.Where(r => EF.Property<DateTimeOffset?>(r, "schedule_publish_at") <= cutoff)
            .Select(r => EF.Property<Guid>(r, "document_id"))
            .ToList()
            .ShouldBe([due]);
        documents.OrderBy(r => EF.Property<DateTimeOffset>(r, "created_at")).ToList().Count.ShouldBe(2);
        db.ReadStrings("select distinct typeof(created_at) from cms_document").ShouldBe(["integer"]);
    }

    [Fact]
    public void MaxLengthIsNotEnforcedByTheDatabase()
    {
        _context.AddLocale(Guid.CreateVersion7(), new string('x', 500), "long");
        _context.SaveChanges();

        _db.ReadStrings("select length(title) || '' from cms_article_i18n").ShouldBe(["500"]);
        _db.ReadStrings("select sql from sqlite_master where name = 'cms_article_i18n'").Single().ShouldContain("\"title\" TEXT NOT NULL");
    }

    [Fact]
    public void PlainDecimalIsComparedAndSortedNumericallyThroughProviderFunctions()
    {
        foreach (var rating in new[] { 100m, 9.5m, 10.25m })
        {
            var id = _context.AddShared();
            _context.ChangeTracker.Entries().Single(e => (Guid)e.Property("document_id").CurrentValue! == id).Property("rating").CurrentValue = rating;
        }

        _context.SaveChanges();

        decimal? cutoff = 10m;
        var articles = _context.Rows("cms_article");
        var sorted = articles.OrderBy(r => EF.Property<decimal?>(r, "rating")).Select(r => EF.Property<decimal?>(r, "rating"));
        var filtered = articles.Where(r => EF.Property<decimal?>(r, "rating") > cutoff).Select(r => EF.Property<decimal?>(r, "rating"));

        sorted.ToList().ShouldBe([9.5m, 10.25m, 100m]);
        filtered.ToList().ShouldBe([100m, 10.25m], ignoreOrder: true);
        sorted.ToQueryString().ShouldContain("COLLATE EF_DECIMAL");
        filtered.ToQueryString().ShouldContain("ef_compare(");
        _db.ReadStrings("select distinct typeof(rating) from cms_article").ShouldBe(["text"]);
    }
}
