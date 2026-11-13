using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Content;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Storage;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Query;

public sealed class PopulationTests : IDisposable
{
    private readonly QueryDatabase _database = new();
    private readonly Guid _article;
    private readonly Guid _author;
    private readonly Guid _company;
    private readonly Guid _firstTag;
    private readonly Guid _secondTag;
    private readonly Guid _media = Guid.CreateVersion7();

    public PopulationTests()
    {
        _article = _database.AddArticle("Hello", new()
        {
            ["views"] = 7L,
            ["seo_meta_title"] = "Seo",
            ["sections"] = """[{"type":"quote","text":"Hi"}]""",
            ["metadata"] = """{"a":1}""",
            ["secret"] = "hush",
        });
        _author = _database.AddAuthor("Ann", secret: "hush");
        _company = _database.AddCompany("Acme");
        _firstTag = _database.AddTag("one");
        _secondTag = _database.AddTag("two");

        _database.Relate("article", _article, "author", "author", _author);
        _database.Relate("article", _article, "tags", "tag", _secondTag, position: 1);
        _database.Relate("article", _article, "tags", "tag", _firstTag, position: 0);
        _database.Relate("article", _article, "hero", "media", _media);
        _database.Relate("author", _author, "company", "company", _company);
        _database.Relate("company", _company, "owner", "author", _author);
    }

    public void Dispose() => _database.Dispose();

    private async Task<ContentDocument> Entry(string query, IContentQueryAccess? access = null) =>
        (await _database.RunAsync($"locale=en&{query}", access)).Items.Single();

    private static ContentDocument Doc(object? value) => value.ShouldBeOfType<ContentDocument>();

    private static void ShouldBeReference(object? value, Guid id, string type)
    {
        var reference = Doc(value);
        reference.Keys.ShouldBe(["id", "type"]);
        reference["id"].ShouldBe(id);
        reference["type"].ShouldBe(type);
    }

    [Fact]
    public async Task AnEntryHasTheSystemFieldsAndValuesOfTheResponseShape()
    {
        var entry = await Entry("sort=title");

        entry.Keys.Take(7).ShouldBe(["id", "status", "locale", "version", "createdAt", "updatedAt", "publishedAt"]);
        entry["id"].ShouldBe(_article);
        entry["status"].ShouldBe(EntryStatus.Published);
        entry["locale"].ShouldBe("en");
        entry["version"].ShouldBe(2);
        entry["title"].ShouldBe("Hello");
        entry["views"].ShouldBe(7L);
        entry["subtitle"].ShouldBeNull();
        Doc(entry["seo"])["metaTitle"].ShouldBe("Seo");
        entry["body"].ShouldBe(RichText.Empty);
        entry["metadata"].ShouldBeOfType<JsonObject>()["a"]!.GetValue<int>().ShouldBe(1);
        entry["sections"].ShouldBeOfType<JsonArray>().Count.ShouldBe(1);
        entry.ContainsKey("createdBy").ShouldBeFalse();
    }

    // The pending schedules that cms_document mirrors are fields of the Management shape alone (#3094).
    [Fact]
    public async Task OnlyTheManagementShapeHasThePendingSchedulesOfTheLocale()
    {
        var at = new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero);
        var document = _database.Context.Documents.Single(item => item.DocumentId == _article && item.Locale == "en");
        document.ScheduleUnpublishAt = at;
        _database.Context.SaveChanges();

        var plain = await Entry("sort=title");
        var unknown = await Should.ThrowAsync<ValidationException>(() => Entry("fields=scheduledUnpublishAt"));
        _database.Services.GetRequiredService<ManagementShape>().Asked = true;
        var managed = await Entry("sort=title");
        var selected = await Entry("fields=title,scheduledUnpublishAt");

        plain.ContainsKey("scheduledPublishAt").ShouldBeFalse();
        plain.ContainsKey("scheduledUnpublishAt").ShouldBeFalse();
        unknown.Errors.ShouldHaveSingleItem().Code.ShouldBe(QueryErrorCodes.UnknownField);
        managed.Keys.Take(9).ShouldBe(["id", "status", "locale", "version", "createdAt", "updatedAt", "publishedAt", "scheduledPublishAt", "scheduledUnpublishAt"]);
        managed["scheduledPublishAt"].ShouldBeNull();
        managed["scheduledUnpublishAt"].ShouldBe(at);
        selected.Keys.ShouldBe(["id", "scheduledUnpublishAt", "title"], ignoreOrder: true);
    }

    [Fact]
    public async Task AComponentWithNoValuesIsNull()
    {
        var other = _database.AddArticle("Other");

        (await _database.RunAsync($"locale=en&filter[id][eq]={other}")).Items.Single()["seo"].ShouldBeNull();
    }

    [Fact]
    public async Task WithoutPopulateARelationIsItsIdAndType()
    {
        var entry = await Entry("sort=title");

        ShouldBeReference(entry["author"], _author, "author");
        ShouldBeReference(entry["hero"], _media, "media");
        var tags = entry["tags"].ShouldBeAssignableTo<IReadOnlyList<ContentDocument>>()!;
        tags.Count.ShouldBe(2);
        ShouldBeReference(tags[0], _firstTag, "tag");
        ShouldBeReference(tags[1], _secondTag, "tag");
        entry["related"].ShouldBeAssignableTo<IReadOnlyList<ContentDocument>>()!.ShouldBeEmpty();
    }

    [Fact]
    public async Task PopulateInlinesOneLevelByDefault()
    {
        var entry = await Entry("populate=author,tags");

        var author = Doc(entry["author"]);
        author["id"].ShouldBe(_author);
        author["type"].ShouldBe("author");
        author["name"].ShouldBe("Ann");
        ShouldBeReference(author["company"], _company, "company");
        author.ContainsKey("status").ShouldBeFalse();

        var tags = entry["tags"].ShouldBeAssignableTo<IReadOnlyList<ContentDocument>>()!;
        tags.Select(tag => tag["name"]).ShouldBe(["one", "two"]);
    }

    [Fact]
    public async Task PopulateAllInlinesEveryRelationButNotMedia()
    {
        var entry = await Entry("populate=*");

        Doc(entry["author"])["name"].ShouldBe("Ann");
        entry["tags"].ShouldBeAssignableTo<IReadOnlyList<ContentDocument>>()!.Select(tag => tag["name"]).ShouldBe(["one", "two"]);
        ShouldBeReference(entry["hero"], _media, "media");
    }

    [Fact]
    public async Task ADepthGoesThatManyLevelsThenStopsAtAReference()
    {
        var entry = await Entry("populate=*&depth=2");

        var company = Doc(Doc(entry["author"])["company"]);
        company["name"].ShouldBe("Acme");
        ShouldBeReference(company["owner"], _author, "author");
    }

    [Fact]
    public async Task ADottedPathGoesBeyondTheDefaultDepth()
    {
        var entry = await Entry("populate=author.company");

        Doc(Doc(entry["author"])["company"])["name"].ShouldBe("Acme");
        ShouldBeReference(entry["hero"], _media, "media");
    }

    [Fact]
    public async Task TheDepthCapIsEnforced()
    {
        var chain = BuildChain();

        var entry = await Entry($"filter[id][eq]={chain.Article}&populate=*&depth=3");

        var author = Doc(entry["author"]);
        var company = Doc(author["company"]);
        var owner = Doc(company["owner"]);
        owner["name"].ShouldBe("Second");
        ShouldBeReference(owner["company"], chain.SecondCompany, "company");

        var exception = await Should.ThrowAsync<ValidationException>(() => _database.RunAsync("locale=en&populate=*&depth=4"));
        exception.Errors.Single().Code.ShouldBe(QueryErrorCodes.LimitExceeded);
        exception.Errors.Single().Path.ShouldBe("depth");
    }

    [Fact]
    public async Task ADottedPathBeyondTheCapIsRejected()
    {
        var exception = await Should.ThrowAsync<ValidationException>(() => _database.RunAsync("locale=en&populate=author.company.owner.company"));

        exception.Errors.Single().Code.ShouldBe(QueryErrorCodes.LimitExceeded);
    }

    [Fact]
    public async Task ACapCanBeConfigured()
    {
        var options = new ContentQueryOptions { MaxPopulateDepth = 1 };

        await Should.ThrowAsync<ValidationException>(() => _database.RunAsync("locale=en&populate=*&depth=2", options: options));

        var entry = (await _database.RunAsync("locale=en&populate=*", options: options)).Items.Single();
        ShouldBeReference(Doc(entry["author"])["company"], _company, "company");
    }

    [Fact]
    public async Task ACycleStopsAtAnEntryAlreadyOnThePath()
    {
        var entry = await Entry("populate=*&depth=3");

        var author = Doc(entry["author"]);
        var company = Doc(author["company"]);

        ShouldBeReference(company["owner"], _author, "author");
    }

    [Fact]
    public async Task ASelfReferenceStaysAReference()
    {
        _database.Relate("article", _article, "related", "article", _article);

        var entry = await Entry("populate=related&depth=3");

        var related = entry["related"].ShouldBeAssignableTo<IReadOnlyList<ContentDocument>>()!;
        ShouldBeReference(related.Single(), _article, "article");
    }

    [Fact]
    public async Task TheSameTargetUnderTwoEntriesIsInlinedForBoth()
    {
        var other = _database.AddArticle("Other");
        _database.Relate("article", other, "author", "author", _author);

        var items = (await _database.RunAsync("locale=en&populate=author&sort=title")).Items;

        items.Select(item => Doc(item["author"])["name"]).ShouldBe(["Ann", "Ann"]);
        items[0]["author"].ShouldNotBeSameAs(items[1]["author"]);
    }

    [Fact]
    public async Task AnUnpublishedOrTrashedTargetIsNotReturnedByAPublishedRead()
    {
        var draft = _database.AddAuthor("Draft", state: ContentState.Draft);
        var other = _database.AddArticle("Other");
        _database.Relate("article", other, "author", "author", draft);
        var trashed = _database.AddArticle("Third");
        _database.Relate("article", trashed, "author", "author", _author);
        _database.Trash(_author);

        var items = (await _database.RunAsync("locale=en&populate=author&sort=title")).Items;

        items.Single(item => item["title"] as string == "Other")["author"].ShouldBeNull();
        items.Single(item => item["title"] as string == "Third")["author"].ShouldBeNull();
    }

    [Fact]
    public async Task ATargetInAnotherStateOrLocaleIsNotReturned()
    {
        var draftOnly = _database.AddAuthor("Draft reference");
        _database.Relate("article", _article, "author", "author", draftOnly, state: ContentState.Draft);

        var entry = await Entry("sort=title");

        ShouldBeReference(entry["author"], _author, "author");
    }

    [Fact]
    public async Task AHiddenFieldIsLeftOutAtEveryLevel()
    {
        var entry = await Entry("populate=author");

        entry.ContainsKey("secret").ShouldBeFalse();
        Doc(entry["author"]).ContainsKey("secret").ShouldBeFalse();
    }

    [Fact]
    public async Task AHiddenFieldInsideABlockOrComponentIsLeftOut()
    {
        var id = _database.AddArticle("Nested", new()
        {
            ["sections"] = """[{"type":"hero","heading":"Hi","internal":"hush"}]""",
            ["seo_internal"] = "hush",
            ["seo_meta_title"] = "Seo",
            ["faq"] = """[{"metaTitle":"Q","internal":"hush"}]""",
        });

        var entry = await Entry($"filter[id][eq]={id}");

        entry["sections"]!.ToString()!.ShouldNotContain("hush");
        entry["sections"]!.ToString()!.ShouldContain("Hi");
        entry["faq"]!.ToString()!.ShouldNotContain("hush");
        Doc(entry["seo"]).ContainsKey("internal").ShouldBeFalse();
    }

    [Fact]
    public async Task ALocalizedRelationIsReadInTheRequestedLocale()
    {
        _database.AddArticle("Bonjour", id: _article, locale: "fr");
        _database.Relate("article", _article, "sponsor", "author", _author, locale: "fr");

        ShouldBeReference((await _database.RunAsync("locale=fr")).Items.Single()["sponsor"], _author, "author");
        (await Entry("sort=title"))["sponsor"].ShouldBeNull();
    }

    [Fact]
    public async Task APopulatedLocalizedTargetIsReadInTheRequestedLocale()
    {
        _database.AddArticle("Bonjour", id: _article, locale: "fr");
        var parent = _database.AddArticle("Parent");
        _database.AddArticle("Parent fr", id: parent, locale: "fr");
        _database.Relate("article", parent, "related", "article", _article);

        var entry = (await _database.RunAsync($"locale=fr&populate=related&filter[id][eq]={parent}")).Items.Single();

        entry["related"].ShouldBeAssignableTo<IReadOnlyList<ContentDocument>>()![0]["title"].ShouldBe("Bonjour");
    }

    [Fact]
    public async Task AFieldPermissionLeavesTheFieldOutOfAPopulatedEntry()
    {
        var access = new QueryAccess();
        access.DeniedFields.Add("author.name");
        access.DeniedFields.Add("article.views");

        var entry = await Entry("populate=author", access);

        entry.ContainsKey("views").ShouldBeFalse();
        var author = Doc(entry["author"]);
        author.ContainsKey("name").ShouldBeFalse();
        author["id"].ShouldBe(_author);
    }

    [Fact]
    public async Task AFieldPermissionLeavesARelationOutWithItsTarget()
    {
        var access = new QueryAccess();
        access.DeniedFields.Add("article.author");

        (await Entry("populate=*", access)).ContainsKey("author").ShouldBeFalse();
    }

    [Fact]
    public async Task ATargetOfATypeThePrincipalCannotReadIsNull()
    {
        var access = new QueryAccess();
        access.DeniedTypes.Add("author");
        access.DeniedTypes.Add("tag");

        foreach (var query in new[] { "populate=*", "sort=title" })
        {
            var entry = await Entry(query, access);

            entry["author"].ShouldBeNull();
            entry["tags"].ShouldBeAssignableTo<IReadOnlyList<ContentDocument>>()!.ShouldBeEmpty();
            ShouldBeReference(entry["hero"], _media, "media");
        }
    }

    [Fact]
    public async Task ATargetARowRuleDeniesIsNullWhetherPopulatedOrNot()
    {
        var access = new QueryAccess();
        access.RowRules["author"] = new FieldFilter("name", FilterOperator.Equal, "Somebody else");

        (await Entry("populate=author", access))["author"].ShouldBeNull();
        (await Entry("sort=title", access))["author"].ShouldBeNull();
    }

    [Fact]
    public async Task ARowRuleKeepsOnlyTheTargetsItAllowsOfAMultipleRelation()
    {
        var access = new QueryAccess();
        access.RowRules["tag"] = new FieldFilter("name", FilterOperator.Equal, "two");

        var tags = (await Entry("sort=title", access))["tags"].ShouldBeAssignableTo<IReadOnlyList<ContentDocument>>()!;
        ShouldBeReference(tags.Single(), _secondTag, "tag");

        var populated = (await Entry("populate=tags", access))["tags"].ShouldBeAssignableTo<IReadOnlyList<ContentDocument>>()!;
        populated.Single()["name"].ShouldBe("two");
    }

    [Fact]
    public async Task ADeniedTargetBeyondTheDepthIsNullToo()
    {
        var access = new QueryAccess();
        access.DeniedTypes.Add("company");

        Doc((await Entry("populate=author", access))["author"])["company"].ShouldBeNull();
    }

    [Fact]
    public async Task ATargetARowRuleAllowsStaysAsItWas()
    {
        var access = new QueryAccess();
        access.RowRules["author"] = new FieldFilter("name", FilterOperator.Equal, "Ann");

        ShouldBeReference((await Entry("sort=title", access))["author"], _author, "author");
        Doc((await Entry("populate=author", access))["author"])["name"].ShouldBe("Ann");
    }

    [Fact]
    public async Task FieldSelectionKeepsTheIdAndWhatIsNamed()
    {
        var entry = await Entry("fields=title,seo.metaTitle,publishedAt");

        entry.Keys.ShouldBe(["id", "publishedAt", "title", "seo"]);
        Doc(entry["seo"]).Keys.ShouldBe(["metaTitle"]);
    }

    [Fact]
    public async Task FieldSelectionAppliesToPopulatedEntries()
    {
        var entry = await Entry("fields=title,author.name,tags&populate=author,tags");

        entry.Keys.ShouldBe(["id", "title", "author", "tags"]);
        Doc(entry["author"]).Keys.ShouldBe(["id", "type", "name"]);
        entry["tags"].ShouldBeAssignableTo<IReadOnlyList<ContentDocument>>()![0].Keys.ShouldBe(["id", "type", "name"]);
    }

    [Fact]
    public async Task FieldSelectionCanNameASystemFieldOfAPopulatedEntry()
    {
        var entry = await Entry("fields=author.createdBy,author.company.name&populate=author.company");

        var author = Doc(entry["author"]);
        author.Keys.ShouldBe(["id", "type", "createdBy", "company"]);
        Doc(author["company"]).Keys.ShouldBe(["id", "type", "name"]);
    }

    [Fact]
    public async Task ASelectedRelationThatIsNotPopulatedIsAReference()
    {
        var entry = await Entry("fields=author.name");

        ShouldBeReference(entry["author"], _author, "author");
    }

    private (Guid Article, Guid SecondCompany) BuildChain()
    {
        var article = _database.AddArticle("Chain");
        var first = _database.AddAuthor("First");
        var firstCompany = _database.AddCompany("First company");
        var second = _database.AddAuthor("Second");
        var secondCompany = _database.AddCompany("Second company");

        _database.Relate("article", article, "author", "author", first);
        _database.Relate("author", first, "company", "company", firstCompany);
        _database.Relate("company", firstCompany, "owner", "author", second);
        _database.Relate("author", second, "company", "company", secondCompany);

        return (article, secondCompany);
    }
}
