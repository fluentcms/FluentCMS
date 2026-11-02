using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

public sealed class ReferenceIntegrityTests : IDisposable
{
    private static readonly string[] Cards = ["card"];

    // The article refers to an author that may or may not be readable, and blocks when asked to.
    private static SchemaModel Model(UnpublishedReferencePolicy policy) => SampleSchema.NewBuilder()
        .AddBlock("card", block => block
            .Field("who", "relation", field => field.Relation("author"))
            .Field("internal", "relation", field => field.Hidden().Relation("author")))
        .AddCollection("author", type => type.Localized().Field("name", "text", field => field.Localizable()))
        .AddCollection("article", type => type
            .Localized()
            .PublishPolicy(policy)
            .Field("title", "text", field => field.Localizable())
            .Field("author", "relation", field => field.Relation("author"))
            .Field("secretAuthor", "relation", field => field.Hidden().Relation("author"))
            .Field("cards", "blocks", field => field.Setting(FieldSettings.Blocks, Cards))
            .Field("body", "richText"))
        .Build();

    private readonly ServiceDatabase _warning = new(schema: Model(UnpublishedReferencePolicy.Warn));
    private readonly ServiceDatabase _blocking = new(schema: Model(UnpublishedReferencePolicy.Block));

    public void Dispose()
    {
        _warning.Dispose();
        _blocking.Dispose();
    }

    private static Task<Guid> AuthorAsync(ServiceDatabase database, string locale = "en") =>
        database.CreateAsync(Editor, new() { ["name"] = "Ann" }, "author", locale).ContinueWith(task => IdOf(task.Result), TaskScheduler.Default);

    private static async Task<Guid> PublishedAuthorAsync(ServiceDatabase database)
    {
        var id = await AuthorAsync(database);
        await database.PublishAsync(Editor, id, type: "author");
        return id;
    }

    private static async Task<Guid> TrashedAuthorAsync(ServiceDatabase database)
    {
        var id = await PublishedAuthorAsync(database);
        await database.DeleteAsync(Editor, id, "author");
        return id;
    }

    private static Task<Guid> ArticleAsync(ServiceDatabase database, ContentDocument values) =>
        database.CreateAsync(Editor, values).ContinueWith(task => IdOf(task.Result), TaskScheduler.Default);

    private static Principal Publisher(ServiceDatabase database, params string[] extra)
    {
        List<(string, PermissionConstraints?)> grants = [("content.article.read", null), ("content.article.publish", null), .. extra.Select(permission => (permission, (PermissionConstraints?)null))];
        return WithRole(database.Database.AddRoleForTest(Guid.NewGuid().ToString("N"), [.. grants]));
    }

    [Fact]
    public async Task AReferenceToAPublishedEntryIsNoWarning()
    {
        var author = await PublishedAuthorAsync(_warning);
        var id = await ArticleAsync(_warning, new() { ["title"] = "One", ["author"] = author });

        (await _warning.PublishAsync(Editor, id)).Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task AReferenceToAnUnpublishedEntryWarnsAndPublishes()
    {
        var author = await AuthorAsync(_warning);
        var id = await ArticleAsync(_warning, new() { ["title"] = "One", ["author"] = author });

        var result = await _warning.PublishAsync(Editor, id);

        var warning = result.Warnings.ShouldHaveSingleItem();
        warning.Path.ShouldBe("author");
        warning.Code.ShouldBe(PublishWarningCodes.UnpublishedReference);
        warning.TargetId.ShouldBe(author);
        warning.TargetType.ShouldBe("author");
        result.Entry["status"].ShouldBe(EntryStatus.Published);
    }

    [Fact]
    public async Task AReferenceToAnUnpublishedEntryBlocksWhenTheTypeSaysSo()
    {
        var author = await AuthorAsync(_blocking);
        var id = await ArticleAsync(_blocking, new() { ["title"] = "One", ["author"] = author });
        var before = _blocking.Trace(id);

        var error = (await Should.ThrowAsync<ValidationException>(() => _blocking.PublishAsync(Editor, id))).Errors.ShouldHaveSingleItem();

        error.Path.ShouldBe("author");
        error.Code.ShouldBe(PublishWarningCodes.UnpublishedReference);
        _blocking.Trace(id).ShouldBe(before);

        await _blocking.PublishAsync(Editor, author, type: "author");
        (await _blocking.PublishAsync(Editor, id)).Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task ATrashedTargetAndOneThatIsNotThereReadTheSameAndNameNoId()
    {
        var trashed = await TrashedAuthorAsync(_warning);
        var missing = Guid.CreateVersion7();
        var one = await ArticleAsync(_warning, new() { ["title"] = "One", ["author"] = trashed });
        var two = await ArticleAsync(_warning, new() { ["title"] = "Two", ["author"] = missing });

        var first = (await _warning.PublishAsync(Editor, one)).Warnings.ShouldHaveSingleItem();
        var second = (await _warning.PublishAsync(Editor, two)).Warnings.ShouldHaveSingleItem();

        first.ShouldBe(second);
        first.Code.ShouldBe(PublishWarningCodes.UnavailableReference);
        first.TargetId.ShouldBeNull();
    }

    [Fact]
    public async Task ATargetOfAnotherSiteOrTypeIsNotThere()
    {
        var elsewhere = IdOf(await _warning.CreateAsync(Editor, new() { ["name"] = "Ann" }, "author", site: SiteB));
        var article = await ArticleAsync(_warning, new() { ["title"] = "Other type" });
        var one = await ArticleAsync(_warning, new() { ["title"] = "One", ["author"] = elsewhere });
        var two = await ArticleAsync(_warning, new() { ["title"] = "Two", ["author"] = article });

        (await _warning.PublishAsync(Editor, one)).Warnings.ShouldHaveSingleItem().Code.ShouldBe(PublishWarningCodes.UnavailableReference);
        (await _warning.PublishAsync(Editor, two)).Warnings.ShouldHaveSingleItem().Code.ShouldBe(PublishWarningCodes.UnavailableReference);
    }

    [Fact]
    public async Task AReferenceInsideABlockOrRichTextCounts()
    {
        var author = await AuthorAsync(_warning);
        var body = new RichText("{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"x\",\"marks\":[{\"type\":\"entryLink\",\"attrs\":{\"entryType\":\"author\",\"entryId\":\"" + author + "\"}}]}]}]}");
        var id = await ArticleAsync(_warning, new()
        {
            ["title"] = "One",
            ["cards"] = new object[] { new ContentDocument { ["type"] = "card", ["who"] = author } },
            ["body"] = body,
        });

        var warnings = (await _warning.PublishAsync(Editor, id)).Warnings;

        warnings.Select(warning => warning.Path).ShouldBe(["body", "cards"], ignoreOrder: true);
        warnings.ShouldAllBe(warning => warning.TargetId == author);
    }

    [Fact]
    public async Task ATargetPublishedInAFallbackLocaleCountsAndOneOnlyElsewhereDoesNot()
    {
        var author = await PublishedAuthorAsync(_warning);
        var fr = IdOf(await _warning.CreateAsync(Editor, new() { ["title"] = "Un", ["author"] = author }, locale: "fr"));
        (await _warning.PublishAsync(Editor, fr, "fr")).Warnings.ShouldBeEmpty();

        var french = await AuthorAsync(_warning, "fr");
        await _warning.PublishAsync(Editor, french, "fr", type: "author");
        var en = IdOf(await _warning.CreateAsync(Editor, new() { ["title"] = "One", ["author"] = french }));
        (await _warning.PublishAsync(Editor, en)).Warnings.ShouldHaveSingleItem().Code.ShouldBe(PublishWarningCodes.UnpublishedReference);
    }

    // The probe: a write takes any id, so anything publish says about a target it may not read would be an oracle.
    [Fact]
    public async Task ATargetOfATypeItMayNotReadIsNoWarningWhateverItIs()
    {
        var published = await PublishedAuthorAsync(_warning);
        var draft = await AuthorAsync(_warning);
        var trashed = await TrashedAuthorAsync(_warning);
        var publisher = Publisher(_warning);

        foreach (var target in new[] { published, draft, trashed, Guid.CreateVersion7() })
        {
            var id = await ArticleAsync(_warning, new() { ["title"] = "One", ["author"] = target });
            (await _warning.PublishAsync(publisher, id)).Warnings.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task UnderBlockATargetOfATypeItMayNotReadIsAlwaysTheSameRefusal()
    {
        var published = await PublishedAuthorAsync(_blocking);
        var draft = await AuthorAsync(_blocking);
        var trashed = await TrashedAuthorAsync(_blocking);
        var publisher = Publisher(_blocking);
        List<string> answers = [];

        foreach (var target in new[] { published, draft, trashed, Guid.CreateVersion7() })
        {
            var id = await ArticleAsync(_blocking, new() { ["title"] = "One", ["author"] = target });
            var error = (await Should.ThrowAsync<ValidationException>(() => _blocking.PublishAsync(publisher, id))).Errors.ShouldHaveSingleItem();
            error.Path.ShouldBe("");
            error.Message.ShouldNotContain(target.ToString());
            answers.Add($"{error.Path}|{error.Code}|{error.Message}");
        }

        answers.Distinct().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ATargetItsRowRuleRefusesReadsTheSameAsOneThatIsNotThere()
    {
        var theirs = await PublishedAuthorAsync(_warning);
        var role = _warning.Database.AddRoleForTest(
            "Own authors",
            ("content.article.read", null),
            ("content.article.publish", null),
            ("content.author.read", new PermissionConstraints { RowRule = BuiltInRoles.OwnEntriesRule }));
        var publisher = WithRole(role);
        var refused = await ArticleAsync(_warning, new() { ["title"] = "One", ["author"] = theirs });
        var missing = await ArticleAsync(_warning, new() { ["title"] = "Two", ["author"] = Guid.CreateVersion7() });

        var first = (await _warning.PublishAsync(publisher, refused)).Warnings.ShouldHaveSingleItem();
        var second = (await _warning.PublishAsync(publisher, missing)).Warnings.ShouldHaveSingleItem();

        first.ShouldBe(second);
        first.TargetId.ShouldBeNull();
    }

    [Fact]
    public async Task AReferenceInAFieldItMayNotReadIsNoWarningAndUnderBlockSaysNotWhere()
    {
        var draft = await AuthorAsync(_warning);
        var limited = WithRole(_warning.Database.AddRoleForTest(
            "Titles",
            ("content.article.read", new PermissionConstraints { Fields = ["title"] }),
            ("content.article.publish", null),
            ("content.author.read", null)));
        var id = await ArticleAsync(_warning, new() { ["title"] = "One", ["author"] = draft });
        (await _warning.PublishAsync(limited, id)).Warnings.ShouldBeEmpty();

        var blocked = await AuthorAsync(_blocking);
        var limitedThere = WithRole(_blocking.Database.AddRoleForTest(
            "Titles",
            ("content.article.read", new PermissionConstraints { Fields = ["title"] }),
            ("content.article.publish", null),
            ("content.author.read", null)));
        var other = await ArticleAsync(_blocking, new() { ["title"] = "One", ["author"] = blocked });
        (await Should.ThrowAsync<ValidationException>(() => _blocking.PublishAsync(limitedThere, other))).Errors.ShouldHaveSingleItem().Path.ShouldBe("");
    }

    [Fact]
    public async Task AHiddenReferenceIsNoWarningAtTheTopOrInsideABlock()
    {
        var draft = await AuthorAsync(_warning);
        var publisher = Publisher(_warning, "content.author.read");
        var id = IdOf(await _warning.AsAsync(Principal.System, services => Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IContentService>(services).CreateAsync(
            "article",
            new()
            {
                ["title"] = "One",
                ["secretAuthor"] = draft,
                ["cards"] = new object[] { new ContentDocument { ["type"] = "card", ["internal"] = draft } },
            },
            Token), origin: Origin.Seed));

        (await _warning.PublishAsync(publisher, id)).Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheSystemPrincipalSeesWhichTargetAndWhatIsWrong()
    {
        var trashed = await TrashedAuthorAsync(_warning);
        var id = await ArticleAsync(_warning, new() { ["title"] = "One", ["author"] = trashed });

        var result = await _warning.AsAsync(Principal.System, services => Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IContentService>(services).PublishAsync("article", id, null, null, Token), origin: Origin.Job);

        result.Warnings.ShouldHaveSingleItem().TargetId.ShouldBe(trashed);
    }

    [Fact]
    public async Task APublishedReadLeavesOutATargetThatIsNotPublishedOrIsInTheTrash()
    {
        var published = await PublishedAuthorAsync(_warning);
        var draft = await AuthorAsync(_warning);
        var trashed = await TrashedAuthorAsync(_warning);

        async Task<object?> ReadAuthorAsync(Guid target)
        {
            var id = await ArticleAsync(_warning, new() { ["title"] = "One", ["author"] = target });
            await _warning.PublishAsync(Editor, id);
            return (await _warning.GetAsync(Editor, id, new GetOptions { Status = QueryStatus.Published }))!["author"];
        }

        (await ReadAuthorAsync(published)).ShouldBeOfType<ContentDocument>()["id"].ShouldBe(published);
        (await ReadAuthorAsync(draft)).ShouldBeNull();
        (await ReadAuthorAsync(trashed)).ShouldBeNull();

        // A draft read still shows what its author attached.
        var inDraft = await ArticleAsync(_warning, new() { ["title"] = "Two", ["author"] = draft });
        (await _warning.GetAsync(Editor, inDraft))!["author"].ShouldBeOfType<ContentDocument>()["id"].ShouldBe(draft);
    }

    [Fact]
    public async Task APublishedReadStillNamesATargetPublishedOnlyInAnotherLocale()
    {
        var french = await AuthorAsync(_warning, "fr");
        await _warning.PublishAsync(Editor, french, "fr", type: "author");
        var id = await ArticleAsync(_warning, new() { ["title"] = "One", ["author"] = french });
        await _warning.PublishAsync(Editor, id);

        var author = (await _warning.GetAsync(Editor, id, new GetOptions { Status = QueryStatus.Published, Populate = ["author"] }))!["author"].ShouldBeOfType<ContentDocument>();

        author["id"].ShouldBe(french);
        author.ContainsKey("name").ShouldBeFalse();
    }
}
