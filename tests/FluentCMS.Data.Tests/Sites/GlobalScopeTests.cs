using FluentCMS.Data.Tests.Query;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Sites;

// A ContentScope.Global type has one set of entries, with no site, that every site reads and may relate to.
public sealed class GlobalScopeTests : IDisposable
{
    private readonly QueryDatabase _database = new();
    private readonly Guid _tag;

    public GlobalScopeTests()
    {
        _tag = _database.AddTag("shared");
    }

    public void Dispose() => _database.Dispose();

    [Theory]
    [InlineData("")]
    [InlineData("site=SITE_A")]
    [InlineData("site=SITE_B")]
    public async Task AGlobalTypeIsReadableFromAnySite(string query)
    {
        var result = await _database.RunAsync(query.Replace("SITE_A", QueryDatabase.SiteA.ToString()).Replace("SITE_B", QueryDatabase.SiteB.ToString()), type: "tag");

        result.Items.Single()["name"].ShouldBe("shared");
        result.Items.Single()["id"].ShouldBe(_tag);
    }

    [Fact]
    public void AGlobalTypeHasNoSiteOnItsRow() =>
        _database.Context.Documents.Single(document => document.DocumentId == _tag).SiteId.ShouldBeNull();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AGlobalTypeIsAllowedAsARelationTargetFromEverySite(bool siteA)
    {
        var site = siteA ? QueryDatabase.SiteA : QueryDatabase.SiteB;
        var article = _database.AddArticle("Tagged", site: site);
        _database.Relate("article", article, "tags", "tag", _tag);

        var result = await _database.RunAsync($"locale=en&site={site}&populate=tags");

        var tags = result.Items.Single()["tags"].ShouldBeOfType<List<FluentCMS.Content.ContentDocument>>();
        tags.Single()["id"].ShouldBe(_tag);
        tags.Single()["name"].ShouldBe("shared");
    }

    [Fact]
    public async Task AGlobalTargetIsFilterableThroughARelationPathFromAnotherSite()
    {
        var article = _database.AddArticle("Tagged", site: QueryDatabase.SiteB);
        _database.Relate("article", article, "tags", "tag", _tag);

        var result = await _database.RunAsync($"locale=en&site={QueryDatabase.SiteB}&filter[tags.name][eq]=shared");

        result.Items.Single()["id"].ShouldBe(article);
    }
}
