using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content.Query;
using FluentCMS.Data.Tests.Query;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Authorization;

public sealed class FieldAccessTests : IDisposable
{
    private static readonly PermissionConstraints TitleAndAuthor = new() { Fields = ["title", "author"] };

    private readonly AuthorizationDatabase _database = new();

    private QueryDatabase Rows => _database.Database;

    public void Dispose() => _database.Dispose();

    private static Principal Limited(Guid role) => AuthorizationDatabase.User([role], [TokenScopes.Management]);

    [Fact]
    public async Task AnAllowListHidesFieldsInResults()
    {
        Rows.AddArticle("Hello", new() { ["views"] = 7L });
        var role = _database.AddRole("Limited", null, ("content.article.read", TitleAndAuthor));

        var entry = (await _database.QueryAsync(Limited(role), "locale=en")).Items.Single();

        entry["title"].ShouldBe("Hello");
        entry.ContainsKey("views").ShouldBeFalse();
        entry.ContainsKey("slug").ShouldBeFalse();
        entry.ContainsKey("status").ShouldBeTrue();
    }

    [Theory]
    [InlineData("locale=en&filter[views][eq]=7", QueryErrorCodes.UnknownField)]
    [InlineData("locale=en&filter[or][0][title][eq]=Hello&filter[or][1][views][eq]=7", QueryErrorCodes.UnknownField)]
    [InlineData("locale=en&sort=views", QueryErrorCodes.NotSortable)]
    [InlineData("locale=en&fields=views", QueryErrorCodes.UnknownField)]
    public async Task AnAllowListHidesFieldsFromFiltersAndSorts(string query, string code)
    {
        Rows.AddArticle("Hello", new() { ["views"] = 7L });
        var role = _database.AddRole("Limited", null, ("content.article.read", TitleAndAuthor));

        var refused = await Should.ThrowAsync<ValidationException>(() => _database.QueryAsync(Limited(role), query));

        refused.Errors.Single().Code.ShouldBe(code);
    }

    [Fact]
    public async Task AnAllowListHidesFieldsInPopulationAndInPathsThroughARelation()
    {
        var ann = Rows.AddAuthor("Ann", age: 30);
        var article = Rows.AddArticle("Hello");
        Rows.Relate("article", article, "author", "author", ann);
        var role = _database.AddRole(
            "Limited",
            null,
            ("content.article.read", TitleAndAuthor),
            ("content.author.read", new PermissionConstraints { Fields = ["name"] }));

        var author = (await _database.QueryAsync(Limited(role), "locale=en&populate=author")).Items.Single()["author"].ShouldBeOfType<ContentDocument>();

        author["name"].ShouldBe("Ann");
        author.ContainsKey("age").ShouldBeFalse();

        var refused = await Should.ThrowAsync<ValidationException>(() => _database.QueryAsync(Limited(role), "locale=en&filter[author.age][eq]=30"));
        refused.Errors.Single().Code.ShouldBe(QueryErrorCodes.UnknownField);
    }

    [Fact]
    public async Task ADeniedRelationFieldIsLeftOutWithItsTarget()
    {
        var ann = Rows.AddAuthor("Ann");
        var article = Rows.AddArticle("Hello");
        Rows.Relate("article", article, "author", "author", ann);
        var role = _database.AddRole("Limited", null, ("content.article.read", new PermissionConstraints { Fields = ["title"] }), ("content.author.read", null));

        var entry = (await _database.QueryAsync(Limited(role), "locale=en&populate=*")).Items.Single();

        entry.ContainsKey("author").ShouldBeFalse();
    }

    [Fact]
    public async Task ATargetTheEvaluatorDeniesIsNullAndDoesNotMatchAFilter()
    {
        var mine = Guid.CreateVersion7();
        var other = Rows.AddAuthor("Other", createdBy: Guid.CreateVersion7());
        var own = Rows.AddAuthor("Own", createdBy: mine);
        var article = Rows.AddArticle("Hello", createdBy: mine);
        var second = Rows.AddArticle("Second", createdBy: mine);
        Rows.Relate("article", article, "author", "author", other);
        Rows.Relate("article", second, "author", "author", own);
        var author = AuthorizationDatabase.User([BuiltInRoles.Author], id: mine);

        var items = (await _database.QueryAsync(author, "locale=en&populate=author&sort=title")).Items;

        items[0]["author"].ShouldBeNull();
        items[1]["author"].ShouldBeOfType<ContentDocument>()["name"].ShouldBe("Own");

        (await _database.QueryAsync(author, $"locale=en&filter[author][eq]={other}")).Items.ShouldBeEmpty();
        (await _database.QueryAsync(author, $"locale=en&filter[author][eq]={own}")).Items.Single()["title"].ShouldBe("Second");
    }

    [Fact]
    public async Task ATargetOfATypeTheRoleDoesNotGrantIsNullWithoutPopulate()
    {
        var ann = Rows.AddAuthor("Ann");
        var article = Rows.AddArticle("Hello");
        Rows.Relate("article", article, "author", "author", ann);
        var role = _database.AddRole("Articles only", null, ("content.article.read", null));

        (await _database.QueryAsync(Limited(role), "locale=en")).Items.Single()["author"].ShouldBeNull();
        (await _database.QueryAsync(Limited(role), $"locale=en&filter[author][eq]={ann}")).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReadsOmitWhatTheAllowListAndTheSchemaHide()
    {
        var role = _database.AddRole("Limited", null, ("content.article.read", new PermissionConstraints { Fields = ["title", "author", "tags", "seo"] }), ("content.author.read", new PermissionConstraints { Fields = ["name"] }));
        var permissions = await _database.PermissionsAsync(AuthorizationDatabase.User([role]));
        var access = new FieldAccess(permissions, QueryDatabase.Schema);

        var document = new ContentDocument
        {
            ["id"] = Guid.CreateVersion7(),
            ["createdBy"] = Guid.CreateVersion7(),
            ["title"] = "Hello",
            ["views"] = 7L,
            ["secret"] = "hush",
            ["seo"] = new ContentDocument { ["metaTitle"] = "Seo", ["internal"] = "hush" },
            ["author"] = new ContentDocument { ["id"] = Guid.CreateVersion7(), ["type"] = "author", ["name"] = "Ann", ["age"] = 30L },
            ["tags"] = new List<ContentDocument> { new() { ["id"] = Guid.CreateVersion7(), ["type"] = "tag", ["name"] = "one" } },
        };

        access.OmitUnreadable(document, AuthorizationDatabase.Article, AuthorizationDatabase.InSiteA);

        document.Keys.ShouldBe(["id", "title", "seo", "author", "tags"]);
        document["seo"].ShouldBeOfType<ContentDocument>().Keys.ShouldBe(["metaTitle"]);
        document["author"].ShouldBeOfType<ContentDocument>().Keys.ShouldBe(["id", "type", "name"]);
        document["tags"].ShouldBeOfType<List<ContentDocument>>().ShouldBeEmpty();
    }

    [Fact]
    public async Task ReadsOmitATargetWhoseTypeCannotBeRead()
    {
        var role = _database.AddRole("Limited", null, ("content.article.read", null));
        var access = new FieldAccess(await _database.PermissionsAsync(AuthorizationDatabase.User([role])), QueryDatabase.Schema);
        var document = new ContentDocument { ["author"] = new ContentDocument { ["id"] = Guid.CreateVersion7(), ["type"] = "author" } };

        access.OmitUnreadable(document, AuthorizationDatabase.Article, AuthorizationDatabase.InSiteA);

        document["author"].ShouldBeNull();
    }

    [Fact]
    public async Task ReadsOmitHiddenValuesInsideBlocksComponentsAndFlatComponents()
    {
        var access = new FieldAccess(await _database.PermissionsAsync(AuthorizationDatabase.User([BuiltInRoles.Editor])), QueryDatabase.Schema);
        var document = new ContentDocument
        {
            ["seo"] = new ContentDocument { ["metaTitle"] = "Seo", ["internal"] = "hush", ["extra"] = "hush" },
            ["faq"] = JsonNode.Parse("""[{"metaTitle":"Q","internal":"hush","extra":"hush"}]"""),
            ["sections"] = JsonSerializer.SerializeToElement(JsonNode.Parse("""[{"type":"hero","heading":"Hi","internal":"hush","extra":"hush"},{"type":"gone","text":"hush"}]""")),
        };

        access.OmitUnreadable(document, AuthorizationDatabase.Article, AuthorizationDatabase.InSiteA);

        document["seo"].ShouldBeOfType<ContentDocument>().Keys.ShouldBe(["metaTitle"]);
        document["faq"]!.ToString()!.ShouldNotContain("hush");
        document["faq"]!.ToString()!.ShouldContain("Q");
        document["sections"]!.ToString()!.ShouldNotContain("hush");
        document["sections"]!.ToString()!.ShouldContain("Hi");
    }

    [Fact]
    public async Task ReadsDropAKeyThatIsNotAFieldAndJsonThatCannotBeRead()
    {
        var role = _database.AddRole("Limited", null, ("content.article.read", new PermissionConstraints { Fields = ["title", "sections"] }));
        var access = new FieldAccess(await _database.PermissionsAsync(AuthorizationDatabase.User([role])), QueryDatabase.Schema);
        var document = new ContentDocument
        {
            ["title"] = "Hello",
            ["fromAHook"] = "hush",
            ["sections"] = "{not json",
        };

        access.OmitUnreadable(document, AuthorizationDatabase.Article, AuthorizationDatabase.InSiteA);

        document.Keys.ShouldBe(["title", "sections"]);
        document["sections"].ShouldBeNull();
    }

    private async Task<FieldAccess> Editor() =>
        new(await _database.PermissionsAsync(AuthorizationDatabase.User([BuiltInRoles.Editor], [TokenScopes.Management])), QueryDatabase.Schema);

    private static string[] Refused(FieldAccess access, ContentDocument changes, WritableFields? writable = null) =>
        [.. Should.Throw<ValidationException>(() => access.RejectUnwritable(changes, AuthorizationDatabase.Article, writable ?? WritableFields.All)).Errors.Select(error => error.Path)];

    [Fact]
    public async Task AWriteOfAFieldOutsideWhatTheGrantsAllowIsRejected()
    {
        var access = await Editor();
        var writable = new WritableFields(true, new HashSet<string> { "title" });

        access.RejectUnwritable(new ContentDocument { ["title"] = "New" }, AuthorizationDatabase.Article, writable);

        Refused(access, new ContentDocument { ["title"] = "New", ["views"] = 1L, ["slug"] = "new" }, writable).ShouldBe(["views", "slug"]);

        var refused = Should.Throw<ValidationException>(() => access.RejectUnwritable(new ContentDocument { ["views"] = 1L }, AuthorizationDatabase.Article, writable));
        refused.Errors.ShouldAllBe(error => error.Code == AuthorizationErrorCodes.FieldNotWritable);
    }

    [Fact]
    public async Task AHiddenFieldAndAFieldThatDoesNotExistAreRefusedAlike()
    {
        var access = await Editor();

        var refused = Should.Throw<ValidationException>(() => access.RejectUnwritable(
            new ContentDocument { ["secret"] = "x", ["missing"] = "x" },
            AuthorizationDatabase.Article,
            WritableFields.All));

        refused.Errors[0].Message.ShouldBe("'secret' is not a field of article that can be written.");
        refused.Errors[1].Message.ShouldBe("'missing' is not a field of article that can be written.");
        refused.Errors[0].Code.ShouldBe(refused.Errors[1].Code);
    }

    public static TheoryData<string, string, string> NestedJson => new()
    {
        { "seo", """{"metaTitle":"ok","internal":"x"}""", "seo.internal" },
        { "seo", """{"metaTitle":"ok","nosuch":"x"}""", "seo.nosuch" },
        { "faq", """[{"metaTitle":"ok"},{"internal":"x"}]""", "faq.internal" },
        { "faq", """[{"nosuch":"x"}]""", "faq.nosuch" },
        { "sections", """[{"type":"quote","text":"ok"},{"type":"hero","heading":"ok","internal":"x"}]""", "sections.internal" },
        { "sections", """[{"type":"hero","nosuch":"x"}]""", "sections.nosuch" },
        { "sections", """[{"type":"gone","internal":"x"}]""", "sections.type" },
        { "sections", """[{"internal":"x"}]""", "sections.type" },
    };

    [Theory]
    [MemberData(nameof(NestedJson))]
    public async Task ANestedFieldIsRefusedInEveryFormTheCoercionAccepts(string field, string json, string path)
    {
        var access = await Editor();
        var node = JsonNode.Parse(json);

        Refused(access, new ContentDocument { [field] = node }).ShouldBe([path]);
        Refused(access, new ContentDocument { [field] = JsonSerializer.SerializeToElement(node) }).ShouldBe([path]);
        Refused(access, new ContentDocument { [field] = json }).ShouldBe([path]);
    }

    [Fact]
    public async Task TheWholeOfADeserializedDocumentIsInspected()
    {
        var access = await Editor();
        var document = JsonSerializer.Deserialize<ContentDocument>("""{"title":"ok","seo":{"internal":"x"},"faq":[{"internal":"x"}],"sections":[{"type":"hero","internal":"x"}]}""")!;

        Refused(access, document).ShouldBe(["seo.internal", "faq.internal", "sections.internal"]);
    }

    [Fact]
    public async Task DictionariesAndDocumentsAreInspected()
    {
        var access = await Editor();

        Refused(access, new ContentDocument { ["seo"] = new ContentDocument { ["internal"] = "x" } }).ShouldBe(["seo.internal"]);
        Refused(access, new ContentDocument { ["seo"] = new Dictionary<string, object?> { ["internal"] = "x" } }).ShouldBe(["seo.internal"]);
        Refused(access, new ContentDocument { ["seo"] = new Dictionary<string, string> { ["internal"] = "x" } }).ShouldBe(["seo.internal"]);
        Refused(access, new ContentDocument { ["faq"] = new List<Dictionary<string, object?>> { new() { ["internal"] = "x" } } }).ShouldBe(["faq.internal"]);
        Refused(access, new ContentDocument { ["sections"] = new List<ContentDocument> { new() { ["type"] = "hero", ["internal"] = "x" } } }).ShouldBe(["sections.internal"]);
    }

    [Fact]
    public async Task AShapeThatCannotBeInspectedIsRefused()
    {
        var access = await Editor();

        Refused(access, new ContentDocument { ["seo"] = new FluentCMS.Core.Tests.Schema.Samples.Seo() }).ShouldBe(["seo"]);
        Refused(access, new ContentDocument { ["seo"] = 5 }).ShouldBe(["seo"]);
        Refused(access, new ContentDocument { ["seo"] = "{not json" }).ShouldBe(["seo"]);
        Refused(access, new ContentDocument { ["seo"] = "\"text\"" }).ShouldBe(["seo"]);
        Refused(access, new ContentDocument { ["seo"] = JsonSerializer.SerializeToElement("""{"internal":"x"}""") }).ShouldBe(["seo"]);
        Refused(access, new ContentDocument { ["faq"] = new object[] { 5 } }).ShouldBe(["faq"]);
        Refused(access, new ContentDocument { ["sections"] = new BlockList([new FluentCMS.Core.Tests.Schema.Samples.QuoteBlock()]) }).ShouldBe(["sections"]);
        Refused(access, new ContentDocument { ["seo"] = new Dictionary<int, object?> { [1] = "x" } }).ShouldBe(["seo"]);
    }

    [Fact]
    public async Task WhatIsAllowedPassesInEveryForm()
    {
        var access = await Editor();
        var changes = JsonSerializer.Deserialize<ContentDocument>("""{"title":"ok","seo":{"metaTitle":"ok"},"faq":[{"metaTitle":"ok"}],"sections":[{"type":"quote","text":"ok"}]}""")!;

        access.RejectUnwritable(changes, AuthorizationDatabase.Article, WritableFields.All);
        access.RejectUnwritable(new ContentDocument { ["seo"] = null, ["faq"] = JsonDocument.Parse("null").RootElement, ["sections"] = "[]", ["version"] = 3 }, AuthorizationDatabase.Article, WritableFields.All);
    }

    [Fact]
    public async Task TheSystemPrincipalMaySetAHiddenField()
    {
        var access = new FieldAccess(await _database.PermissionsAsync(Principal.System), QueryDatabase.Schema);

        access.RejectUnwritable(new ContentDocument { ["secret"] = "x", ["seo"] = new ContentDocument { ["internal"] = "x" } }, AuthorizationDatabase.Article, WritableFields.All);
    }

    [Fact]
    public async Task TheAuthorFieldsCannotBeWritten()
    {
        var access = await Editor();

        Refused(access, new ContentDocument { ["createdBy"] = Guid.CreateVersion7(), ["updatedBy"] = Guid.CreateVersion7(), ["version"] = 3 }).ShouldBe(["createdBy", "updatedBy"]);
    }

    [Fact]
    public async Task WithoutTheOperationNothingCanBeWritten()
    {
        var access = await Editor();

        Refused(access, new ContentDocument { ["title"] = "New" }, WritableFields.None).ShouldBe(["title"]);
    }
}
