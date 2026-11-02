using FluentCMS.Content;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Localization;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Data.Tests.Query;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using FluentCMS.Sites;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Sites;

// What counts as a missing value for the field shapes that are not plain text: a localizable component and
// localizable relations.
public sealed class FallbackShapeTests : IDisposable
{
    private static readonly SchemaModel Model = SampleSchema.NewBuilder()
        .AddComponent("seo", component => component.Field("metaTitle", "text").Field("metaDescription", "text"))
        .AddCollection("author", type => type.Field("name", "text"))
        .AddCollection("page", type => type
            .Localized()
            .Field("title", "text", field => field.Localizable())
            .Field("seo", "component", field => field.Localizable().Setting(FieldSettings.Component, "seo"))
            .Field("sponsor", "relation", field => field.Localizable().Relation("author"))
            .Field("related", "relation", field => field.Localizable().Multiple().Relation("author")))
        .Build();

    private static readonly SiteId Site = new(QueryDatabase.SiteA);

    private readonly QueryDatabase _database = new(schema: Model);
    private readonly Guid _page = Guid.CreateVersion7();
    private readonly Guid _ann;
    private readonly Guid _bob;

    public FallbackShapeTests()
    {
        var context = _database.Context;
        context.Add(new Site { Id = QueryDatabase.SiteA, Name = "A", Slug = "a", DefaultLocale = "en", CreatedAt = DateTimeOffset.UtcNow });
        context.AddRange(
            new SiteLocale { SiteId = QueryDatabase.SiteA, Code = "en", Position = 0 },
            new SiteLocale { SiteId = QueryDatabase.SiteA, Code = "fr", Position = 1 },
            new SiteLocale { SiteId = QueryDatabase.SiteA, Code = "fr-CA", Position = 2 });
        context.SaveChanges();

        _ann = _database.AddAuthor("Ann");
        _bob = _database.AddAuthor("Bob");

        AddPage("en", "Home", seoTitle: "SEO en");
        AddPage("fr", "Accueil", seoTitle: "SEO fr");
        AddPage("fr-CA", "Salut", seoTitle: null);

        _database.Relate("page", _page, "sponsor", "author", _ann, locale: "fr");
        _database.Relate("page", _page, "related", "author", _bob, locale: "fr");
    }

    public void Dispose() => _database.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private void AddPage(string locale, string title, string? seoTitle)
    {
        var context = _database.Context;
        var now = DateTimeOffset.UtcNow;
        context.Documents.Add(new Document
        {
            DocumentId = _page,
            Locale = locale,
            Type = "page",
            SiteId = QueryDatabase.SiteA,
            Status = EntryStatus.Published,
            DraftVersion = 1,
            PublishedVersion = 1,
            PublishedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        });

        if (locale == "en")
        {
            context.Rows("cms_page").Add(new Dictionary<string, object> { ["document_id"] = _page, ["site_id"] = QueryDatabase.SiteA, ["state"] = ContentState.Published });
        }

        var row = new Dictionary<string, object>
        {
            ["document_id"] = _page,
            ["site_id"] = QueryDatabase.SiteA,
            ["locale"] = locale,
            ["state"] = ContentState.Published,
            ["title"] = title,
        };

        if (seoTitle is not null)
        {
            row["seo_meta_title"] = seoTitle;
        }

        context.Rows("cms_page_i18n").Add(row);
        context.SaveChanges();
    }

    private async Task<ContentDocument> ReadAsync(string query)
    {
        var parsed = BracketQueryParser.Parse(query) with { Site = Site };
        var engine = _database.Engine();
        var reader = new LocaleFallbackReader(engine, Model, new LocaleRegistry(_database.Context), new ContentQueryOptions());

        var result = await engine.ExecuteAsync("page", parsed, Token);
        await reader.ApplyAsync("page", parsed, result.Items, await reader.ChainAsync(Site, parsed.Locale!, parsed.Fallback, Token), Token);
        return result.Items.Single();
    }

    [Fact]
    public async Task AComponentWithNoValuesInTheRequestedLocaleFallsBack()
    {
        var entry = await ReadAsync("locale=fr-CA");

        var seo = entry["seo"].ShouldBeOfType<ContentDocument>();
        seo["metaTitle"].ShouldBe("SEO fr");
        entry["fallbacks"].ShouldBeOfType<ContentDocument>()["seo"].ShouldBe("fr");
    }

    [Fact]
    public async Task AComponentWithAValueInTheRequestedLocaleStays()
    {
        var entry = await ReadAsync("locale=fr");

        entry["seo"].ShouldBeOfType<ContentDocument>()["metaTitle"].ShouldBe("SEO fr");
        entry.ContainsKey("fallbacks").ShouldBeFalse();
    }

    [Fact]
    public async Task ALocalizedRelationWithNoReferencesInTheRequestedLocaleFallsBack()
    {
        var entry = await ReadAsync("locale=fr-CA");

        var sponsor = entry["sponsor"].ShouldBeOfType<ContentDocument>();
        (sponsor["id"], sponsor["type"]).ShouldBe((_ann, "author"));
        entry["fallbacks"].ShouldBeOfType<ContentDocument>()["sponsor"].ShouldBe("fr");
    }

    [Fact]
    public async Task AMultipleLocalizedRelationWithNoReferencesFallsBack()
    {
        var entry = await ReadAsync("locale=fr-CA");

        var related = entry["related"].ShouldBeOfType<List<ContentDocument>>();
        related.Select(item => item["id"]).ShouldBe([_bob]);
        entry["fallbacks"].ShouldBeOfType<ContentDocument>()["related"].ShouldBe("fr");
    }

    [Fact]
    public async Task APopulatedFallbackRelationIsInlined()
    {
        var entry = await ReadAsync("locale=fr-CA&populate=sponsor");

        entry["sponsor"].ShouldBeOfType<ContentDocument>()["name"].ShouldBe("Ann");
    }
}
