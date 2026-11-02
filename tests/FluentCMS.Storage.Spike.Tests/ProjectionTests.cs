using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace FluentCMS.Storage.Spike.Tests;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(HeroBlock), "hero")]
[JsonDerivedType(typeof(QuoteBlock), "quote")]
public abstract class SpikeBlock;

public sealed class HeroBlock : SpikeBlock
{
    public string Heading { get; set; } = "";
}

public sealed class QuoteBlock : SpikeBlock
{
    public string Text { get; set; } = "";
}

public sealed class ProjectionTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SpikeDatabase _db = new();
    private readonly SpikeDbContext _context;
    private readonly Guid _first;
    private readonly Guid _second;

    public ProjectionTests()
    {
        _context = _db.CreateMigratedContext();

        _first = _context.AddShared(
            category: "news",
            sections: JsonSerializer.Serialize<SpikeBlock[]>([new HeroBlock { Heading = "Welcome" }, new QuoteBlock { Text = "Hi" }], JsonOptions),
            publishedOn: new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero));
        _context.AddLocale(_first, "Hello", "hello");
        _context.AddLocale(_first, "Bonjour", "bonjour", locale: "fr");
        _context.AddDocument(_first, status: "published", draftVersion: 7);
        _context.AddDocument(_first, locale: "fr", status: "draft", draftVersion: 2);

        _second = _context.AddShared(category: "video", publishedOn: new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero));
        _context.AddLocale(_second, "Second", "second");
        _context.AddDocument(_second);

        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Dispose();
        _db.Dispose();
    }

    [Fact]
    public void LinqJoinProjectsSharedLocaleAndDocumentRowsIntoATypedClass()
    {
        var query =
            from a in _context.Rows("cms_article")
            join t in _context.Rows("cms_article_i18n")
                on new { Id = EF.Property<Guid>(a, "document_id"), State = EF.Property<int>(a, "state") }
                equals new { Id = EF.Property<Guid>(t, "document_id"), State = EF.Property<int>(t, "state") }
            join d in _context.Rows(SpikeDbContext.Document)
                on new { Id = EF.Property<Guid>(t, "document_id"), Locale = EF.Property<string>(t, "locale") }
                equals new { Id = EF.Property<Guid>(d, "document_id"), Locale = EF.Property<string>(d, "locale") }
            where EF.Property<int>(a, "state") == 1 && EF.Property<string>(t, "locale") == "fr"
            select new Article
            {
                DocumentId = EF.Property<Guid>(a, "document_id"),
                State = EF.Property<int>(a, "state"),
                Locale = EF.Property<string>(t, "locale"),
                Status = EF.Property<string>(d, "status"),
                Version = EF.Property<int>(d, "draft_version"),
                Title = EF.Property<string>(t, "title"),
                Slug = EF.Property<string>(t, "slug"),
                Category = EF.Property<string>(a, "category"),
                PublishedOn = EF.Property<DateTimeOffset?>(a, "published_on"),
            };

        var article = query.Single();

        article.DocumentId.ShouldBe(_first);
        article.Locale.ShouldBe("fr");
        article.Status.ShouldBe("draft");
        article.Version.ShouldBe(2);
        article.Title.ShouldBe("Bonjour");
        article.Category.ShouldBe("news");
        article.PublishedOn.ShouldBe(new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void RuntimeBuiltProjectionSelectsOnlyTheRequestedColumns()
    {
        var query = ArticleProjection.Joined(_context)
            .Where(r => EF.Property<string>(r.Locale, "locale") == "en")
            .Select(ArticleProjection.Select(nameof(Article.Title), nameof(Article.Category)));

        query.ToQueryString().ReplaceLineEndings("\n").ShouldBe("""
            SELECT "c0"."title" AS "Title", "c"."category" AS "Category"
            FROM "cms_article" AS "c"
            INNER JOIN "cms_article_i18n" AS "c0" ON "c"."document_id" = "c0"."document_id" AND "c"."state" = "c0"."state"
            WHERE "c0"."locale" = 'en'
            """);

        var articles = query.ToList();
        articles.Select(a => a.Title).ShouldBe(["Hello", "Second"], ignoreOrder: true);
        articles.ShouldAllBe(a => a.Slug == null);
    }

    [Fact]
    public void ProjectionComposesWithSortAndPaging()
    {
        var page = ArticleProjection.Joined(_context)
            .Where(r => EF.Property<int>(r.Shared, "state") == 1 && EF.Property<string>(r.Locale, "locale") == "en")
            .OrderByDescending(r => EF.Property<DateTimeOffset?>(r.Shared, "published_on"))
            .ThenBy(r => EF.Property<Guid>(r.Shared, "document_id"))
            .Skip(1)
            .Take(1)
            .Select(ArticleProjection.Select(nameof(Article.DocumentId), nameof(Article.PublishedOn)))
            .ToList();

        page.Single().DocumentId.ShouldBe(_first);
    }

    [Fact]
    public void MissingLocaleRowProjectsNullsThroughALeftJoin()
    {
        var french = _context.Rows("cms_article_i18n").Where(t => EF.Property<string>(t, "locale") == "fr");
        var query =
            from a in _context.Rows("cms_article")
            join t in french
                on new { Id = EF.Property<Guid>(a, "document_id"), State = EF.Property<int>(a, "state") }
                equals new { Id = EF.Property<Guid>(t, "document_id"), State = EF.Property<int>(t, "state") }
                into rows
            from t in rows.DefaultIfEmpty()
            select new Article
            {
                DocumentId = EF.Property<Guid>(a, "document_id"),
                Title = EF.Property<string>(t, "title"),
            };

        var articles = query.ToDictionary(a => a.DocumentId, a => a.Title);

        articles[_first].ShouldBe("Bonjour");
        articles[_second].ShouldBeNull();
    }

    [Fact]
    public void BlockListColumnDeserializesIntoTypedBlocksAfterTheQuery()
    {
        var sections = ArticleProjection.Joined(_context)
            .Where(r => EF.Property<Guid>(r.Shared, "document_id") == _first && EF.Property<string>(r.Locale, "locale") == "en")
            .Select(ArticleProjection.Select(nameof(Article.Sections)))
            .AsEnumerable()
            .Select(a => JsonSerializer.Deserialize<SpikeBlock[]>(a.Sections!, JsonOptions)!)
            .Single();

        sections[0].ShouldBeOfType<HeroBlock>().Heading.ShouldBe("Welcome");
        sections[1].ShouldBeOfType<QuoteBlock>().Text.ShouldBe("Hi");
    }

    [Fact]
    public void RelationPathJoinsThroughTheReferenceTableToTheTargetRowInTheSameStateAndLocale()
    {
        _context.AddReference(_first, "related", _second);
        _context.SaveChanges();

        var term = "Seco";
        var rows = _context.Rows("cms_article_i18n");
        var references = _context.Rows(SpikeDbContext.Reference);
        var query = rows.Where(source =>
            EF.Property<int>(source, "state") == 1
            && EF.Property<string>(source, "locale") == "en"
            && references.Any(reference =>
                EF.Property<string>(reference, "source_type") == "article"
                && EF.Property<Guid>(reference, "source_document_id") == EF.Property<Guid>(source, "document_id")
                && EF.Property<int>(reference, "state") == EF.Property<int>(source, "state")
                && EF.Property<string>(reference, "field") == "related"
                && rows.Any(target =>
                    EF.Property<Guid>(target, "document_id") == EF.Property<Guid>(reference, "target_id")
                    && EF.Property<int>(target, "state") == EF.Property<int>(source, "state")
                    && EF.Property<string>(target, "locale") == EF.Property<string>(source, "locale")
                    && EF.Property<string>(target, "title").Contains(term))));

        query.Select(r => EF.Property<Guid>(r, "document_id")).ToList().ShouldBe([_first]);
    }

    [Fact]
    public void WhatReferencesTargetQueryUsesTheTargetIndex()
    {
        var target = _second;
        var query = _context.Rows(SpikeDbContext.Reference)
            .Where(r => EF.Property<string>(r, "target_type") == "article" && EF.Property<Guid>(r, "target_id") == target);

        SpikeDatabase.QueryPlan(query).ShouldContain("IX_cms_reference_target_type_target_id");
    }
}
