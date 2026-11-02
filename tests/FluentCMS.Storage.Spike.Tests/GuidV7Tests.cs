using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace FluentCMS.Storage.Spike.Tests;

public sealed class GuidV7Tests : IDisposable
{
    private readonly SpikeDatabase _db = new();
    private readonly SpikeDbContext _context;

    public GuidV7Tests() => _context = _db.CreateMigratedContext();

    public void Dispose()
    {
        _context.Dispose();
        _db.Dispose();
    }

    [Fact]
    public void KeyIsGeneratedAsV7WhenTheDictionaryOmitsIt()
    {
        var documentId = Guid.CreateVersion7();
        _context.AddDocument(documentId);
        _context.AddShared(documentId);
        _context.AddLocale(documentId, "Hello", "hello");
        _context.AddReference(documentId, "author", Guid.CreateVersion7());
        _context.SaveChanges();

        foreach (var table in new[] { SpikeDbContext.Document, "cms_article", "cms_article_i18n", SpikeDbContext.Reference })
        {
            var id = (Guid)_context.Rows(table).Single()["id"];
            id.Version.ShouldBe(7);
        }
    }

    [Fact]
    public void KeyIsStoredAsTextAndFindsTheRow()
    {
        _context.AddDocument(Guid.CreateVersion7());
        _context.SaveChanges();
        var id = (Guid)_context.Rows(SpikeDbContext.Document).Single()["id"];

        _db.ReadStrings("select typeof(id) from cms_document").ShouldBe(["text"]);
        _db.ReadStrings("select length(id) || '' from cms_document").ShouldBe(["36"]);

        _context.ChangeTracker.Clear();
        _context.Rows(SpikeDbContext.Document).Find(id).ShouldNotBeNull();
    }

    [Fact]
    public void TextOrderMatchesCreationOrderAcrossMilliseconds()
    {
        var start = new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);
        var created = Enumerable.Range(0, 200).Select(i => Guid.CreateVersion7(start.AddMilliseconds(i))).ToList();

        foreach (var id in created.OrderBy(_ => Guid.NewGuid()))
        {
            _context.AddShared(documentId: id);
        }

        _context.SaveChanges();

        var sorted = _context.Rows("cms_article")
            .OrderBy(r => EF.Property<Guid>(r, "document_id"))
            .Select(r => EF.Property<Guid>(r, "document_id"))
            .ToList();

        sorted.ShouldBe(created);
    }

    [Fact]
    public void WithinOneMillisecondV7IsNotMonotonic()
    {
        var instant = new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);
        var ids = Enumerable.Range(0, 100).Select(_ => Guid.CreateVersion7(instant)).ToList();

        ids.ShouldNotBe(ids.Order().ToList());
    }

    [Fact]
    public void KeysetPredicateOnTheKeyTranslatesAndPages()
    {
        var start = new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);
        var created = Enumerable.Range(0, 5).Select(i => Guid.CreateVersion7(start.AddSeconds(i))).ToList();
        foreach (var id in created)
        {
            _context.AddShared(documentId: id);
        }

        _context.SaveChanges();

        var cursor = created[1];
        var page = _context.Rows("cms_article")
            .Where(r => EF.Property<Guid>(r, "document_id") > cursor)
            .OrderBy(r => EF.Property<Guid>(r, "document_id"))
            .Select(r => EF.Property<Guid>(r, "document_id"))
            .Take(2);

        page.ToQueryString().ShouldContain("""WHERE "c"."document_id" > @cursor""");
        page.ToList().ShouldBe([created[2], created[3]]);
    }
}
