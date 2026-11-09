using FluentCMS.Content;
using FluentCMS.Core.Content.Query;
using System.Globalization;
using FluentCMS.Core.Storage;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Query.QueryDatabase;

namespace FluentCMS.Data.Tests.Query;

// Every operator on the database of the run, reached through the bracket grammar so that text operands are coerced as they will be
// from a client. Alpha and Beta have values; Gamma has none.
public sealed class FilterTests : IDisposable
{
    private readonly QueryDatabase _database = new();
    private readonly Guid _alpha;
    private readonly Guid _ann;
    private readonly Guid _bob;
    private readonly Guid _net;
    private readonly Guid _sql;

    public FilterTests()
    {
        _alpha = _database.AddArticle("Alpha", new()
        {
            ["subtitle"] = "First post",
            ["views"] = 10L,
            ["price"] = 1.5m,
            ["featured"] = true,
            ["published_on"] = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            ["issued"] = new DateOnly(2026, 1, 1),
            ["category"] = "news",
            ["labels"] = """["net","sql"]""",
            ["contact"] = "ann@example.org",
            ["link"] = "https://example.org/a",
            ["summary"] = "About net",
            ["sections"] = """[{"type":"hero","heading":"Welcome","level":2,"score":1.5,"featured":true,"on":"2026-01-01","at":"2026-01-01T10:00:00Z","kinds":["wide"],"internal":"x"},{"type":"quote","text":"Hi"}]""",
            ["seo_meta_title"] = "Seo alpha",
            ["metadata"] = """{"audience":"dev","minutes":12,"rating":4.5,"public":true,"code":"12","labels":["net"],"my-key":"x"}""",
            ["secret"] = "hush",
        });
        var beta = _database.AddArticle("Beta", new()
        {
            ["views"] = 20L,
            ["price"] = 2.25m,
            ["featured"] = false,
            ["published_on"] = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
            ["issued"] = new DateOnly(2026, 2, 1),
            ["category"] = "opinion",
            ["labels"] = """["sql"]""",
            ["contact"] = "bob@example.org",
            ["link"] = "https://example.org/b",
            ["summary"] = "About sql",
            ["sections"] = """[{"type":"quote","text":"Welcome"}]""",
            ["metadata"] = """{"audience":"ops","minutes":3,"public":false}""",
        });
        var gamma = _database.AddArticle("Gamma");

        _database.AddArticle("Delta", state: ContentState.Draft);
        _database.Trash(_database.AddArticle("Trashed"));
        _database.AddArticle("Alpha en français", id: _alpha, locale: "fr");

        var acme = _database.AddCompany("Acme");
        _ann = _database.AddAuthor("Ann", age: 30);
        _bob = _database.AddAuthor("Bob", age: 40);
        _net = _database.AddTag("net");
        _sql = _database.AddTag("sql");

        _database.Relate("author", _ann, "company", "company", acme);
        _database.Relate("article", _alpha, "author", "author", _ann);
        _database.Relate("article", beta, "author", "author", _bob);
        _database.Relate("article", _alpha, "tags", "tag", _net, position: 0);
        _database.Relate("article", _alpha, "tags", "tag", _sql, position: 1);
        _database.Relate("article", beta, "tags", "tag", _sql);
        _database.Relate("article", gamma, "hero", "media", Guid.CreateVersion7());
    }

    public void Dispose() => _database.Dispose();

    private async Task<string[]> Titles(string filter, string locale = "en", IContentQueryAccess? access = null)
    {
        var result = await _database.RunAsync($"{filter}&locale={locale}&sort=title", access);
        return result.Items.Select(item => (string)item["title"]!).ToArray();
    }

    [Theory]
    [InlineData("filter[title][eq]=Alpha", "Alpha")]
    [InlineData("filter[title][ne]=Alpha", "Beta,Gamma")]
    [InlineData("filter[title][in]=Alpha,Gamma", "Alpha,Gamma")]
    [InlineData("filter[title][nin]=Alpha,Gamma", "Beta")]
    [InlineData("filter[title][contains]=lph", "Alpha")]
    [InlineData("filter[title][startsWith]=Be", "Beta")]
    [InlineData("filter[title][null]=true", "")]
    [InlineData("filter[title][notNull]=true", "Alpha,Beta,Gamma")]
    [InlineData("filter[subtitle][eq]=First post", "Alpha")]
    [InlineData("filter[subtitle][ne]=First post", "Beta,Gamma")]
    [InlineData("filter[subtitle][null]=true", "Beta,Gamma")]
    [InlineData("filter[subtitle][notNull]=true", "Alpha")]
    [InlineData("filter[subtitle][null]=false", "Alpha")]
    [InlineData("filter[summary][contains]=sql", "Beta")]
    [InlineData("filter[slug][eq]=beta", "Beta")]
    [InlineData("filter[slug][startsWith]=al", "Alpha")]
    [InlineData("filter[contact][eq]=ann@example.org", "Alpha")]
    [InlineData("filter[contact][contains]=bob@", "Beta")]
    [InlineData("filter[link][startsWith]=https://example.org/b", "Beta")]
    public async Task TextOperators(string filter, string expected) =>
        (await Titles(filter)).ShouldBe(Split(expected));

    [Theory]
    [InlineData("filter[views][eq]=10", "Alpha")]
    [InlineData("filter[views][ne]=10", "Beta,Gamma")]
    [InlineData("filter[views][in]=10,20", "Alpha,Beta")]
    [InlineData("filter[views][nin]=10", "Beta,Gamma")]
    [InlineData("filter[views][lt]=20", "Alpha")]
    [InlineData("filter[views][lte]=20", "Alpha,Beta")]
    [InlineData("filter[views][gt]=10", "Beta")]
    [InlineData("filter[views][gte]=10", "Alpha,Beta")]
    [InlineData("filter[views][between]=11,20", "Beta")]
    [InlineData("filter[views][null]=true", "Gamma")]
    [InlineData("filter[views][notNull]=true", "Alpha,Beta")]
    [InlineData("filter[price][eq]=1.5", "Alpha")]
    [InlineData("filter[price][lt]=2", "Alpha")]
    [InlineData("filter[price][gte]=2.25", "Beta")]
    [InlineData("filter[price][between]=1,2", "Alpha")]
    [InlineData("filter[price][in]=1.5,2.25", "Alpha,Beta")]
    [InlineData("filter[featured][eq]=true", "Alpha")]
    [InlineData("filter[featured][ne]=true", "Beta,Gamma")]
    [InlineData("filter[featured][null]=true", "Gamma")]
    public async Task NumberAndBooleanOperators(string filter, string expected) =>
        (await Titles(filter)).ShouldBe(Split(expected));

    [Theory]
    [InlineData("filter[issued][eq]=2026-01-01", "Alpha")]
    [InlineData("filter[issued][lt]=2026-02-01", "Alpha")]
    [InlineData("filter[issued][gte]=2026-02-01", "Beta")]
    [InlineData("filter[issued][between]=2025-12-01,2026-01-15", "Alpha")]
    [InlineData("filter[publishedOn][eq]=2026-01-01T00:00:00Z", "Alpha")]
    [InlineData("filter[publishedOn][gt]=2026-01-01T00:00:00Z", "Beta")]
    [InlineData("filter[publishedOn][lte]=2026-01-01T01:00:00%2B01:00", "Alpha")]
    [InlineData("filter[publishedOn][between]=2026-01-15T00:00:00Z,2026-02-15T00:00:00Z", "Beta")]
    [InlineData("filter[publishedOn][null]=true", "Gamma")]
    public async Task DateOperators(string filter, string expected) =>
        (await Titles(filter)).ShouldBe(Split(expected));

    [Theory]
    [InlineData("filter[category][eq]=news", "Alpha")]
    [InlineData("filter[category][ne]=news", "Beta,Gamma")]
    [InlineData("filter[category][in]=news,opinion", "Alpha,Beta")]
    [InlineData("filter[category][contains]=pin", "Beta")]
    [InlineData("filter[labels][contains]=net", "Alpha")]
    [InlineData("filter[labels][in]=net,sql", "Alpha,Beta")]
    [InlineData("filter[labels][nin]=net", "Beta,Gamma")]
    [InlineData("filter[labels][null]=true", "Gamma")]
    [InlineData("filter[labels][notNull]=true", "Alpha,Beta")]
    public async Task SelectOperators(string filter, string expected) =>
        (await Titles(filter)).ShouldBe(Split(expected));

    [Theory]
    [InlineData("filter[seo.metaTitle][eq]=Seo alpha", "Alpha")]
    [InlineData("filter[seo.metaTitle][null]=true", "Beta,Gamma")]
    [InlineData("filter[body][null]=true", "Alpha,Beta,Gamma")]
    public async Task ComponentAndRichTextOperators(string filter, string expected) =>
        (await Titles(filter)).ShouldBe(Split(expected));

    [Theory]
    [InlineData("filter[author.name][eq]=Ann", "Alpha")]
    [InlineData("filter[author.name][ne]=Ann", "Beta,Gamma")]
    [InlineData("filter[author.name][nin]=Ann", "Beta,Gamma")]
    [InlineData("filter[author.name][nin]=Ann,Bob", "Gamma")]
    [InlineData("filter[author.name][null]=true", "Gamma")]
    [InlineData("filter[author.name][notNull]=true", "Alpha,Beta")]
    [InlineData("filter[author.age][eq]=30", "Alpha")]
    [InlineData("filter[author.age][ne]=30", "Beta,Gamma")]
    [InlineData("filter[author.age][in]=30,40", "Alpha,Beta")]
    [InlineData("filter[author.age][nin]=30", "Beta,Gamma")]
    [InlineData("filter[author.age][lt]=40", "Alpha")]
    [InlineData("filter[author.age][lte]=40", "Alpha,Beta")]
    [InlineData("filter[author.age][gt]=30", "Beta")]
    [InlineData("filter[author.age][gte]=30", "Alpha,Beta")]
    [InlineData("filter[author.age][between]=31,40", "Beta")]
    [InlineData("filter[author.age][null]=true", "Gamma")]
    [InlineData("filter[author.age][notNull]=true", "Alpha,Beta")]
    [InlineData("filter[author.company.name][ne]=Acme", "Beta,Gamma")]
    [InlineData("filter[author.company.name][null]=true", "Beta,Gamma")]
    [InlineData("filter[author.company][null]=true", "Beta,Gamma")]
    [InlineData("filter[author.company][notNull]=true", "Alpha")]
    [InlineData("filter[tags.name][ne]=net", "Beta,Gamma")]
    [InlineData("filter[tags.name][ne]=sql", "Gamma")]
    [InlineData("filter[tags.name][nin]=net", "Beta,Gamma")]
    [InlineData("filter[not][tags.name][eq]=net", "Beta,Gamma")]
    [InlineData("filter[not][author.name][ne]=Ann", "Alpha")]
    [InlineData("filter[author.name][contains]=o", "Beta")]
    [InlineData("filter[author.name][startsWith]=A", "Alpha")]
    [InlineData("filter[author.name][in]=Ann,Bob", "Alpha,Beta")]
    [InlineData("filter[author.company.name][eq]=Acme", "Alpha")]
    [InlineData("filter[tags.name][eq]=sql", "Alpha,Beta")]
    [InlineData("filter[tags.name][eq]=net", "Alpha")]
    [InlineData("filter[author][null]=true", "Gamma")]
    [InlineData("filter[author][notNull]=true", "Alpha,Beta")]
    public async Task RelationPathOperators(string filter, string expected) =>
        (await Titles(filter)).ShouldBe(Split(expected));

    [Fact]
    public async Task RelationIdOperators()
    {
        (await Titles($"filter[id][in]={_alpha}")).ShouldBe(["Alpha"]);
        (await Titles($"filter[id][ne]={_alpha}")).ShouldBe(["Beta", "Gamma"]);
        (await Titles($"filter[author][eq]={_ann}")).ShouldBe(["Alpha"]);
        (await Titles($"filter[author][in]={_ann},{_bob}")).ShouldBe(["Alpha", "Beta"]);
        (await Titles($"filter[tags][eq]={_sql}")).ShouldBe(["Alpha", "Beta"]);
    }

    [Fact]
    public async Task MediaIsFilteredByItsReference()
    {
        var media = Guid.CreateVersion7();
        _database.Relate("article", _alpha, "hero", "media", media);

        (await Titles($"filter[hero][eq]={media}")).ShouldBe(["Alpha"]);
    }

    [Fact]
    public async Task ARelationPathReadsTheTargetInTheSameState()
    {
        var draftAuthor = _database.AddAuthor("Draft author", state: ContentState.Draft);
        _database.Relate("article", _alpha, "author", "author", draftAuthor, position: 1);

        (await Titles("filter[author.name][eq]=Draft author")).ShouldBeEmpty();
    }

    [Fact]
    public async Task ARelationPathSkipsATrashedTarget()
    {
        _database.Trash(_ann);

        (await Titles("filter[author.name][eq]=Ann")).ShouldBeEmpty();
    }

    [Fact]
    public async Task ARelationPathReadsTheTargetInTheRequestedLocaleWithoutFallback()
    {
        var parent = _database.AddArticle("Parent", new());
        _database.Relate("article", parent, "related", "article", _alpha);

        (await Titles("filter[related.title][eq]=Alpha")).ShouldBe(["Parent"]);
        (await Titles("filter[related.title][eq]=Alpha en français")).ShouldBeEmpty();
    }

    [Fact]
    public async Task ALocalizedRelationMatchesTheRequestedLocaleOnly()
    {
        _database.Relate("article", _alpha, "sponsor", "author", _ann, locale: "fr");

        (await Titles("filter[sponsor.name][eq]=Ann", locale: "fr")).ShouldBe(["Alpha en français"]);
        (await Titles("filter[sponsor.name][eq]=Ann", locale: "en")).ShouldBeEmpty();
        (await Titles("filter[sponsor][notNull]=true", locale: "en")).ShouldBeEmpty();
    }

    [Fact]
    public async Task AnEntryWithATargetThatHasNoValueIsMissingAValue()
    {
        var carol = _database.AddAuthor("Carol");
        var delta = _database.AddArticle("Omega");
        _database.Relate("article", delta, "author", "author", carol);

        (await Titles("filter[author.age][null]=true")).ShouldBe(["Gamma", "Omega"]);
    }

    [Fact]
    public async Task AnyOperatorOfTheTargetWorksThroughARelation()
    {
        var parent = _database.AddArticle("Parent");
        _database.Relate("article", parent, "related", "article", _alpha);

        (await Titles("filter[related.sections][hasBlock]=hero")).ShouldBe(["Parent"]);
        (await Titles("filter[related.sections.hero.heading][eq]=Welcome")).ShouldBe(["Parent"]);
        (await Titles("filter[related.metadata.audience][eq]=dev")).ShouldBe(["Parent"]);
        (await Titles("filter[related.labels][contains]=net")).ShouldBe(["Parent"]);
        (await Titles("filter[related.price][between]=1,2")).ShouldBe(["Parent"]);
        (await Titles("filter[related.featured][eq]=true")).ShouldBe(["Parent"]);
        (await Titles("filter[related.issued][lt]=2026-01-02")).ShouldBe(["Parent"]);
        (await Titles("filter[related.publishedOn][gte]=2026-01-01T00:00:00Z")).ShouldBe(["Parent"]);
        (await Titles("filter[related.title][startsWith]=Al")).ShouldBe(["Parent"]);
        (await Titles("filter[related.title][contains]=lph")).ShouldBe(["Parent"]);
    }

    [Fact]
    public async Task ThreeRelationHopsAreAllowed()
    {
        var owner = _database.AddAuthor("Owner");
        var acme = (Guid)(await _database.RunAsync("filter[name][eq]=Acme", type: "company")).Items.Single()["id"]!;
        _database.Relate("company", acme, "owner", "author", owner);

        (await Titles("filter[author.company.owner.name][eq]=Owner")).ShouldBe(["Alpha"]);
    }

    [Fact]
    public async Task AReferenceOfANonLocalizedFieldHasAnEmptyLocale()
    {
        _database.Relate("article", _alpha, "related", "article", _alpha, locale: "en", position: 2);

        (await Titles("filter[related][notNull]=true")).ShouldBeEmpty();
    }

    [Fact]
    public async Task TextOperatorsAreCaseSensitive()
    {
        (await Titles("filter[title][contains]=ALPHA")).ShouldBeEmpty();
        (await Titles("filter[title][startsWith]=al")).ShouldBeEmpty();
        (await Titles("filter[title][eq]=alpha")).ShouldBeEmpty();
    }

    [Fact]
    public async Task ALikeWildcardInAnOperandIsPlainText()
    {
        (await Titles("filter[title][startsWith]=A%25")).ShouldBeEmpty();
        (await Titles("filter[title][startsWith]=_lpha")).ShouldBeEmpty();
        (await Titles("filter[title][contains]=l%25a")).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("filter[sections][hasBlock]=hero", "Alpha")]
    [InlineData("filter[sections][hasBlock]=quote", "Alpha,Beta")]
    [InlineData("filter[sections][null]=true", "Gamma")]
    [InlineData("filter[sections][notNull]=true", "Alpha,Beta")]
    [InlineData("filter[sections.hero.heading][eq]=Welcome", "Alpha")]
    [InlineData("filter[sections.quote.text][eq]=Welcome", "Beta")]
    [InlineData("filter[sections.hero.heading][ne]=Welcome", "Beta,Gamma")]
    [InlineData("filter[sections.hero.heading][contains]=elc", "Alpha")]
    [InlineData("filter[sections.hero.level][eq]=2", "Alpha")]
    [InlineData("filter[sections.hero.score][eq]=1.5", "Alpha")]
    [InlineData("filter[sections.hero.featured][eq]=true", "Alpha")]
    [InlineData("filter[sections.hero.on][eq]=2026-01-01", "Alpha")]
    [InlineData("filter[sections.hero.at][eq]=2026-01-01T10:00:00Z", "Alpha")]
    [InlineData("filter[sections.hero.at][eq]=2026-01-01T12:00:00%2B02:00", "Alpha")]
    [InlineData("filter[sections.hero.at][eq]=2026-01-01T10:00:00", "Alpha")]
    [InlineData("filter[sections.hero.at][eq]=2026-01-01T10:00:01Z", "")]
    [InlineData("filter[sections.hero.kinds][contains]=wide", "Alpha")]
    public async Task BlockOperators(string filter, string expected) =>
        (await Titles(filter)).ShouldBe(Split(expected));

    [Fact]
    public async Task ATypedDateInABlockComparesAsTheStoredTextInAnyCulture()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("fa-IR");
        try
        {
            var query = new ContentQuery { Locale = "en", Filter = new FieldFilter("sections.hero.on", FilterOperator.Equal, new DateOnly(2026, 1, 1)) };

            (await _database.Engine().ExecuteAsync("article", query, TestContext.Current.CancellationToken)).Items.Single()["title"].ShouldBe("Alpha");
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    // Nothing writes a date and time into JSON yet. The compiler compares with the UTC form of the response shape, so
    // the same instant in any other form is not found, and #2693 must store this one.
    [Fact]
    public async Task ADateAndTimeInABlockMatchesOnlyTheUtcFormOfTheResponseShape()
    {
        _database.AddArticle("Offset", new() { ["sections"] = """[{"type":"hero","at":"2026-03-01T10:00:00+00:00"}]""" });
        _database.AddArticle("Utc", new() { ["sections"] = """[{"type":"hero","at":"2026-03-01T10:00:00Z"}]""" });

        (await Titles("filter[sections.hero.at][eq]=2026-03-01T10:00:00%2B00:00")).ShouldBe(["Utc"]);
    }

    [Theory]
    [InlineData("filter[metadata.audience][eq]=dev", "Alpha")]
    [InlineData("filter[metadata.audience][ne]=dev", "Beta,Gamma")]
    [InlineData("filter[metadata.audience][contains]=p", "Beta")]
    [InlineData("filter[metadata.labels][contains]=net", "Alpha")]
    [InlineData("filter[metadata.minutes][eq]=12", "Alpha")]
    [InlineData("filter[metadata.code][eq]=12", "Alpha")]
    [InlineData("filter[metadata.rating][eq]=4.5", "Alpha")]
    [InlineData("filter[metadata.public][eq]=false", "Beta")]
    [InlineData("filter[metadata][null]=true", "Gamma")]
    [InlineData("filter[metadata][notNull]=true", "Alpha,Beta")]
    public async Task JsonOperators(string filter, string expected) =>
        (await Titles(filter)).ShouldBe(Split(expected));

    [Fact]
    public async Task ATypedOperandOnAJsonKeyKeepsItsType()
    {
        var engine = _database.Engine();
        async Task<int> Count(object value) => (await engine.ExecuteAsync("article", new() { Locale = "en", Filter = new FieldFilter("metadata.code", FilterOperator.Equal, value) }, TestContext.Current.CancellationToken)).Items.Count;

        (await Count(12L)).ShouldBe(0);
        (await Count("12")).ShouldBe(1);
    }

    [Theory]
    [InlineData("id", "Alpha")]
    [InlineData("status", "Alpha,Beta,Gamma")]
    public async Task SystemFieldEquality(string field, string expected)
    {
        var value = field == "id" ? _alpha.ToString() : "published";
        (await Titles($"filter[{field}][eq]={value}")).ShouldBe(Split(expected));
    }

    [Theory]
    [InlineData("filter[createdAt][gt]=2000-01-01T00:00:00Z", "Alpha,Beta,Gamma")]
    [InlineData("filter[createdAt][lt]=2000-01-01T00:00:00Z", "")]
    [InlineData("filter[publishedAt][notNull]=true", "Alpha,Beta,Gamma")]
    [InlineData("filter[publishedAt][null]=true", "")]
    [InlineData("filter[createdAt][null]=true", "")]
    [InlineData("filter[createdAt][notNull]=true", "Alpha,Beta,Gamma")]
    [InlineData("filter[createdAt][between]=2000-01-01T00:00:00Z,2999-01-01T00:00:00Z", "Alpha,Beta,Gamma")]
    [InlineData("filter[status][in]=published,draft", "Alpha,Beta,Gamma")]
    [InlineData("filter[status][ne]=published", "")]
    [InlineData("filter[version][between]=1,3", "Alpha,Beta,Gamma")]
    [InlineData("filter[version][eq]=2", "Alpha,Beta,Gamma")]
    [InlineData("filter[createdBy][eq]=00000000-0000-0000-0000-000000000000", "Alpha,Beta,Gamma")]
    public async Task SystemFieldComparison(string filter, string expected) =>
        (await Titles(filter)).ShouldBe(Split(expected));

    [Theory]
    [InlineData("filter[or][0][title][eq]=Alpha&filter[or][1][title][eq]=Beta", "Alpha,Beta")]
    [InlineData("filter[and][0][views][gt]=5&filter[and][1][views][lt]=15", "Alpha")]
    [InlineData("filter[views][gt]=5&filter[views][lt]=15", "Alpha")]
    [InlineData("filter[not][title][eq]=Alpha", "Beta,Gamma")]
    [InlineData("filter[not][views][eq]=10", "Beta,Gamma")]
    [InlineData("filter[not][or][0][title][eq]=Alpha&filter[not][or][1][title][eq]=Beta", "Gamma")]
    [InlineData("filter[or][0][title][eq]=Alpha&filter[or][0][views][eq]=20&filter[or][1][title][eq]=Beta", "Beta")]
    [InlineData("filter[not][sections][hasBlock]=hero", "Beta,Gamma")]
    [InlineData("filter[not][author.name][eq]=Ann", "Beta,Gamma")]
    public async Task LogicalNodes(string filter, string expected) =>
        (await Titles(filter)).ShouldBe(Split(expected));

    [Fact]
    public async Task ReadsTheRequestedState()
    {
        (await Titles("status=draft", access: new QueryAccess())).ShouldBe(["Delta"]);
        (await Titles("status=published")).ShouldBe(["Alpha", "Beta", "Gamma"]);
    }

    [Fact]
    public async Task AnEntryWithoutARowInTheLocaleIsNotListed() =>
        (await Titles("withTotal=false", locale: "fr")).ShouldBe(["Alpha en français"]);

    [Fact]
    public async Task ReadsTheRequestedSite()
    {
        var other = _database.AddArticle("Other site", site: SiteB);

        (await Titles($"site={SiteB}")).ShouldBe(["Other site"]);
        (await Titles($"site={SiteA}")).ShouldNotContain("Other site");
        (await _database.RunAsync($"site={SiteB}&locale=en")).Items.Single()["id"].ShouldBe(other);
    }

    [Fact]
    public async Task ASiteDoesNotFilterAGlobalType() =>
        (await _database.RunAsync($"site={SiteB}", type: "tag")).Items.Count.ShouldBe(2);

    private static string[] Split(string expected) => expected.Length == 0 ? [] : expected.Split(',');
}
