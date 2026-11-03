using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

// Relations inside blocks and groups and entry links inside rich text, on read, on a version and on the way back in.
public sealed class NestedTargetTests : IDisposable
{
    private static readonly string[] Cards = ["card"];

    private static SchemaModel Model() => SampleSchema.NewBuilder()
        .AddComponent("box", component => component.Field("who", "relation", field => field.Relation("author")).Field("note", "richText"))
        .AddBlock("card", block => block
            .Field("who", "relation", field => field.Relation("author"))
            .Field("many", "relation", field => field.Multiple().Relation("author"))
            .Field("label", "text")
            .Field("note", "richText")
            .Field("box", "component", field => field.Setting(FieldSettings.Component, "box")))
        .AddCollection("vault", type => type.Field("name", "text"))
        .AddCollection("author", type => type.Field("name", "text"))
        .AddCollection("article", type => type
            .Field("title", "text")
            .Field("author", "relation", field => field.Relation("author"))
            .Field("cards", "blocks", field => field.Setting(FieldSettings.Blocks, Cards))
            .Field("other", "blocks", field => field.Setting(FieldSettings.Blocks, Cards))
            .Field("box", "component", field => field.Setting(FieldSettings.Component, "box"))
            .Field("body", "richText"))
        .Build();

    private readonly ServiceDatabase _database = new(schema: Model());

    public void Dispose() => _database.Dispose();

    private static RichText Linked(string text, Guid target, string type = "author") => new(
        "{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"" + text + "\",\"marks\":[{\"type\":\"entryLink\",\"attrs\":{\"entryType\":\"" + type + "\",\"entryId\":\"" + target + "\"}}]}]}]}");

    private static ContentDocument Card(Guid who, string label = "L") => new() { ["type"] = "card", ["who"] = who, ["label"] = label };

    private async Task<Guid> AuthorAsync(string state)
    {
        var id = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "Ann" }, "author"));
        if (state is "published" or "trashed")
        {
            await _database.PublishAsync(Editor, id, type: "author");
        }

        if (state == "trashed")
        {
            await _database.DeleteAsync(Editor, id, "author");
        }

        return id;
    }

    private async Task<Guid> ArticleAsync(ContentDocument values, bool publish = true)
    {
        var id = IdOf(await _database.CreateAsync(Editor, values, "article"));
        if (publish)
        {
            await _database.PublishAsync(Editor, id, type: "article");
        }

        return id;
    }

    private Task<ContentDocument?> PublishedReadAsync(Principal principal, Guid id) =>
        _database.GetAsync(principal, id, new GetOptions { Status = QueryStatus.Published }, "article");

    private static string Text(object? value) => JsonSerializer.Serialize(value);

    private static JsonObject FirstCard(ContentDocument entry) => (JsonObject)((JsonArray)entry["cards"]!)[0]!;

    private Principal Reader(params (string Permission, PermissionConstraints? Constraints)[] extra) =>
        WithRole(_database.Database.AddRoleForTest(Guid.NewGuid().ToString("N"), [("content.article.read", null), .. extra]));

    private Principal Writer(params (string Permission, PermissionConstraints? Constraints)[] extra) =>
        Reader([("content.article.update", null), .. extra]);

    [Fact]
    public async Task AnIdOfAnotherTypeReadsAsNoTargetOnEveryRead()
    {
        var vault = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "v" }, "vault"));
        await _database.PublishAsync(Editor, vault, type: "vault");
        var hidden = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "v" }, "vault"));
        var principal = Reader(("content.author.read", null));

        foreach (var target in new[] { vault, hidden, Guid.CreateVersion7() })
        {
            var id = await ArticleAsync(new() { ["title"] = "x", ["author"] = target });

            (await _database.GetAsync(principal, id, type: "article"))!["author"].ShouldBeNull();
            (await PublishedReadAsync(principal, id))!["author"].ShouldBeNull();
            (await _database.VersionAsync(principal, id, 1, type: "article"))!["author"].ShouldBeNull();
        }
    }

    [Fact]
    public async Task APublishedReadLeavesOutWhatIsNotPublishedAtEveryDepth()
    {
        var ok = await AuthorAsync("published");
        var draft = await AuthorAsync("draft");
        var trashed = await AuthorAsync("trashed");
        var id = await ArticleAsync(new()
        {
            ["title"] = "x",
            ["cards"] = new object[]
            {
                new ContentDocument { ["type"] = "card", ["who"] = trashed, ["many"] = new[] { ok, draft, trashed }, ["note"] = Linked("in block", draft), ["box"] = new ContentDocument { ["who"] = draft } },
                Card(ok),
            },
            ["box"] = new ContentDocument { ["note"] = Linked("in group", trashed) },
            ["body"] = Linked("in body", draft),
        });

        var read = (await PublishedReadAsync(Editor, id))!;

        var text = Text(read);
        text.ShouldNotContain(draft.ToString());
        text.ShouldNotContain(trashed.ToString());
        text.ShouldContain("in block");
        text.ShouldContain("in group");
        text.ShouldContain("in body");

        var card = FirstCard(read);
        card["who"].ShouldBeNull();
        card["many"]!.AsArray().ShouldHaveSingleItem()!["id"]!.GetValue<string>().ShouldBe(ok.ToString());
        card["box"]!["who"].ShouldBeNull();
        ((JsonArray)read["cards"]!)[1]!["who"]!["id"]!.GetValue<string>().ShouldBe(ok.ToString());
        read["body"].ShouldBeOfType<RichText>().Json.ShouldNotContain("entryLink");
    }

    [Fact]
    public async Task ADraftReadKeepsATrashedTargetOfAReadableTypeLikeATopLevelRelation()
    {
        var trashed = await AuthorAsync("trashed");
        var id = await ArticleAsync(new() { ["title"] = "x", ["author"] = trashed, ["cards"] = new object[] { Card(trashed) }, ["body"] = Linked("t", trashed) }, publish: false);

        var read = (await _database.GetAsync(Editor, id, type: "article"))!;

        read["author"].ShouldNotBeNull();
        FirstCard(read)["who"]!["id"]!.GetValue<string>().ShouldBe(trashed.ToString());
        read["body"].ShouldBeOfType<RichText>().Json.ShouldContain(trashed.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ATargetOfATypeItMayNotReadIsNamedNowhere(bool published)
    {
        var author = await AuthorAsync("published");
        var id = await ArticleAsync(new()
        {
            ["title"] = "x",
            ["author"] = author,
            ["box"] = new ContentDocument { ["who"] = author, ["note"] = Linked("g", author) },
            ["cards"] = new object[] { Card(author) },
            ["body"] = Linked("keep me", author),
        });
        var principal = Reader();

        var read = published ? await PublishedReadAsync(principal, id) : await _database.GetAsync(principal, id, type: "article");
        var version = await _database.VersionAsync(principal, id, 1, type: "article");

        foreach (var entry in new[] { read!, version! })
        {
            var text = Text(entry);
            text.ShouldNotContain(author.ToString());
            text.ShouldContain("keep me");
        }
    }

    [Fact]
    public async Task ATargetItsRowRuleRefusesIsNamedNowhereAndItsOwnIs()
    {
        var me = Guid.CreateVersion7();
        var principal = WithRole(_database.Database.AddRoleForTest(
            "Own authors",
            ("content.article.read", null),
            ("content.author.create", null),
            ("content.author.read", new PermissionConstraints { RowRule = BuiltInRoles.OwnEntriesRule })), me);
        var theirs = await AuthorAsync("published");
        var mine = IdOf(await _database.CreateAsync(principal, new() { ["name"] = "Me" }, "author"));
        await _database.PublishAsync(Editor, mine, type: "author");
        var id = await ArticleAsync(new() { ["title"] = "x", ["cards"] = new object[] { Card(theirs), Card(mine) }, ["body"] = Linked("t", theirs) });

        var read = (await PublishedReadAsync(principal, id))!;

        var cards = (JsonArray)read["cards"]!;
        cards[0]!["who"].ShouldBeNull();
        cards[1]!["who"]!["id"]!.GetValue<string>().ShouldBe(mine.ToString());
        read["body"].ShouldBeOfType<RichText>().Json.ShouldNotContain("entryLink");
    }

    [Fact]
    public async Task ALinkToAnUnknownTypeIsDroppedLikeOneToAnUnreadableType()
    {
        var id = await ArticleAsync(new() { ["title"] = "x", ["body"] = Linked("t", Guid.CreateVersion7(), "nosuchtype") });

        (await PublishedReadAsync(Editor, id))!["body"].ShouldBeOfType<RichText>().Json.ShouldNotContain("entryLink");
    }

    [Fact]
    public async Task AFullUpdateOfWhatAReadGaveKeepsTheTargetsItLeftOut()
    {
        var author = await AuthorAsync("published");
        var id = await ArticleAsync(new()
        {
            ["title"] = "One",
            ["cards"] = new object[] { new ContentDocument { ["type"] = "card", ["who"] = author, ["many"] = new[] { author }, ["label"] = "L", ["note"] = Linked("n", author) } },
            ["box"] = new ContentDocument { ["note"] = Linked("g", author) },
            ["body"] = Linked("b", author),
        });
        var writer = Writer();

        var read = (await _database.GetAsync(writer, id, type: "article"))!;
        Text(read).ShouldNotContain(author.ToString());
        read["title"] = "Two";
        var written = await _database.UpdateAsync(writer, id, read, 1, "article");

        Text(written).ShouldNotContain(author.ToString());
        var snapshot = JsonNode.Parse(_database.Versions(id)[^1].Snapshot)!;
        snapshot["title"]!.GetValue<string>().ShouldBe("Two");
        var card = snapshot["cards"]![0]!;
        card["who"]!["id"]!.GetValue<string>().ShouldBe(author.ToString());
        card["many"]![0]!["id"]!.GetValue<string>().ShouldBe(author.ToString());
        card["note"]!.ToJsonString().ShouldContain(author.ToString());
        snapshot["box"]!["note"]!.ToJsonString().ShouldContain(author.ToString());
        snapshot["body"]!.ToJsonString().ShouldContain(author.ToString());
    }

    [Fact]
    public async Task APatchOfWhatAReadGaveKeepsThemToo()
    {
        var author = await AuthorAsync("published");
        var id = await ArticleAsync(new() { ["title"] = "One", ["cards"] = new object[] { Card(author) }, ["body"] = Linked("b", author) });
        var writer = Writer();

        var read = (await _database.GetAsync(writer, id, type: "article"))!;
        await _database.PatchAsync(writer, id, new() { ["cards"] = read["cards"], ["body"] = read["body"] }, 1, "article");

        var snapshot = _database.Versions(id)[^1].Snapshot;
        JsonNode.Parse(snapshot)!["cards"]![0]!["who"]!["id"]!.GetValue<string>().ShouldBe(author.ToString());
        JsonNode.Parse(snapshot)!["body"]!.ToJsonString().ShouldContain(author.ToString());
    }

    [Fact]
    public async Task AnEditedBlockKeepsItsTargetAndAnEditedDocumentLosesTheLink()
    {
        var author = await AuthorAsync("published");
        var id = await ArticleAsync(new() { ["title"] = "One", ["cards"] = new object[] { Card(author, "old") }, ["body"] = Linked("old", author) });
        var writer = Writer();

        var edited = new RichText("""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"new"}]}]}""");
        await _database.PatchAsync(writer, id, new() { ["cards"] = new object[] { new ContentDocument { ["type"] = "card", ["label"] = "new" } }, ["body"] = edited }, 1, "article");

        var snapshot = JsonNode.Parse(_database.Versions(id)[^1].Snapshot)!;
        snapshot["cards"]![0]!["label"]!.GetValue<string>().ShouldBe("new");
        snapshot["cards"]![0]!["who"]!["id"]!.GetValue<string>().ShouldBe(author.ToString());
        snapshot["body"]!.ToJsonString().ShouldNotContain(author.ToString());
    }

    [Fact]
    public async Task ATargetItCanSeeCanStillBeCleared()
    {
        var author = await AuthorAsync("published");
        var id = await ArticleAsync(new() { ["title"] = "One", ["cards"] = new object[] { Card(author) } });
        var writer = Writer(("content.author.read", null));

        await _database.PatchAsync(writer, id, new() { ["cards"] = new object[] { new ContentDocument { ["type"] = "card", ["label"] = "L" } } }, 1, "article");

        JsonNode.Parse(_database.Versions(id)[^1].Snapshot)!["cards"]![0]!["who"].ShouldBeNull();
    }

    [Fact]
    public async Task ARestoreDoesNotBringBackNorLoseANestedTargetItMayNotRead()
    {
        var old = await AuthorAsync("published");
        var current = await AuthorAsync("published");
        var id = await ArticleAsync(new() { ["title"] = "One", ["cards"] = new object[] { Card(old) } }, publish: false);
        await _database.PatchAsync(Editor, id, new() { ["title"] = "Two", ["cards"] = new object[] { Card(current) } }, 1, "article");

        await _database.RestoreVersionAsync(Writer(), id, 1, type: "article");

        var snapshot = JsonNode.Parse(_database.Versions(id)[^1].Snapshot)!;
        snapshot["title"]!.GetValue<string>().ShouldBe("One");
        snapshot["cards"]![0]!["who"]!["id"]!.GetValue<string>().ShouldBe(current.ToString());
    }

    private int CardReferences(Guid id) => _database.References(id, Core.Storage.ContentState.Draft).Count(reference => reference.Field == "cards");

    private static ContentDocument ManyTargets(Guid first, Guid second) => new()
    {
        ["title"] = Guid.NewGuid().ToString("N"),
        ["cards"] = new object[] { new ContentDocument { ["type"] = "card", ["many"] = new[] { first, second } } },
    };

    [Fact]
    public async Task RepeatedSavesOfWhatItCannotSeeLeaveATargetListAsItWas()
    {
        var first = await AuthorAsync("draft");
        var second = await AuthorAsync("draft");
        var id = await ArticleAsync(ManyTargets(first, second), publish: false);
        var writer = Writer();

        var version = 1;
        for (var round = 0; round < 3; round++)
        {
            version = VersionOf(await _database.PatchAsync(writer, id, new() { ["other"] = new object[] { new ContentDocument { ["type"] = "card", ["label"] = $"round {round}" } } }, version, "article"));
            CardReferences(id).ShouldBe(2);
        }

        var read = (await _database.GetAsync(writer, id, type: "article"))!;
        version = VersionOf(await _database.UpdateAsync(writer, id, read, version, "article"));
        CardReferences(id).ShouldBe(2);

        var order = _database.References(id, Core.Storage.ContentState.Draft).Where(reference => reference.Field == "cards").OrderBy(reference => reference.Position).Select(reference => reference.TargetId);
        order.ShouldBe([first, second]);
    }

    [Fact]
    public async Task AFieldItMayNotWriteKeepsItsListThroughAnUpdateAndARestore()
    {
        var first = await AuthorAsync("draft");
        var second = await AuthorAsync("draft");
        var limited = Reader(("content.article.update", new PermissionConstraints { Fields = ["title"] }));

        var updated = await ArticleAsync(ManyTargets(first, second), publish: false);
        await _database.PatchAsync(limited, updated, new() { ["title"] = "Changed" }, 1, "article");
        CardReferences(updated).ShouldBe(2);
        await _database.UpdateAsync(limited, updated, new() { ["title"] = "Changed again" }, 2, "article");
        CardReferences(updated).ShouldBe(2);

        var restored = await ArticleAsync(ManyTargets(first, second), publish: false);
        await _database.PatchAsync(Editor, restored, new() { ["title"] = "Two" }, 1, "article");
        await _database.RestoreVersionAsync(limited, restored, 1, type: "article");
        CardReferences(restored).ShouldBe(2);
    }

    [Fact]
    public async Task AListItemItRemovesThatItCouldSeeStaysRemovedAndAnUnseenOneStays()
    {
        var unseen = await AuthorAsync("draft");
        var seen = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "Seen" }, "author"));
        var owner = Guid.CreateVersion7();
        var writer = WithRole(_database.Database.AddRoleForTest(
            "Own authors",
            ("content.article.read", null),
            ("content.article.update", null),
            ("content.author.create", null),
            ("content.author.read", new PermissionConstraints { RowRule = BuiltInRoles.OwnEntriesRule })), owner);
        var mine = IdOf(await _database.CreateAsync(writer, new() { ["name"] = "Mine" }, "author"));
        var id = await ArticleAsync(new()
        {
            ["title"] = "x",
            ["cards"] = new object[] { new ContentDocument { ["type"] = "card", ["many"] = new[] { unseen, mine, seen } } },
        }, publish: false);

        await _database.PatchAsync(writer, id, new() { ["cards"] = new object[] { new ContentDocument { ["type"] = "card" } } }, 1, "article");

        var targets = _database.References(id, Core.Storage.ContentState.Draft).Where(reference => reference.Field == "cards").Select(reference => reference.TargetId).ToList();
        targets.ShouldContain(unseen);
        targets.ShouldContain(seen);
        targets.ShouldNotContain(mine);
        targets.Count.ShouldBe(2);
    }

    private static RichText InlineBlock(Guid target, string label = "kept") => new(
        "{\"type\":\"doc\",\"content\":[{\"type\":\"inlineBlock\",\"attrs\":{\"blockType\":\"card\",\"data\":{\"label\":\"" + label + "\",\"who\":{\"id\":\"" + target + "\",\"type\":\"author\"}}}}]}");

    [Fact]
    public async Task ARelationInAnInlineBlockIsRecordedCheckedAndFiltered()
    {
        var draft = await AuthorAsync("draft");
        var id = await ArticleAsync(new() { ["title"] = "x", ["body"] = InlineBlock(draft) }, publish: false);

        _database.References(id, Core.Storage.ContentState.Draft).ShouldHaveSingleItem().TargetId.ShouldBe(draft);
        _database.References(id, Core.Storage.ContentState.Draft)[0].Field.ShouldBe("body");

        var warning = (await _database.PublishAsync(Editor, id, type: "article")).Warnings.ShouldHaveSingleItem();
        warning.Path.ShouldBe("body");
        warning.TargetId.ShouldBe(draft);

        var published = (await PublishedReadAsync(Editor, id))!["body"].ShouldBeOfType<RichText>();
        published.Json.ShouldNotContain(draft.ToString());
        published.Json.ShouldContain("kept");
        (await _database.GetAsync(Editor, id, type: "article"))!["body"].ShouldBeOfType<RichText>().Json.ShouldContain(draft.ToString());

        var blind = Reader();
        (await _database.GetAsync(blind, id, type: "article"))!["body"].ShouldBeOfType<RichText>().Json.ShouldNotContain(draft.ToString());
        Text(await _database.VersionAsync(blind, id, 1, type: "article")).ShouldNotContain(draft.ToString());
    }

    [Fact]
    public async Task AnInlineBlockSentBackAsItWasReadKeepsItsTarget()
    {
        var author = await AuthorAsync("published");
        var id = await ArticleAsync(new() { ["title"] = "One", ["body"] = InlineBlock(author) });
        var writer = Writer();

        var read = (await _database.GetAsync(writer, id, type: "article"))!;
        await _database.PatchAsync(writer, id, new() { ["body"] = read["body"] }, 1, "article");

        _database.References(id, Core.Storage.ContentState.Draft).ShouldHaveSingleItem().TargetId.ShouldBe(author);
    }

    [Fact]
    public async Task TheDataOfAnInlineBlockOfAnUnknownTypeIsLeftAlone()
    {
        var body = new RichText("{\"type\":\"doc\",\"content\":[{\"type\":\"inlineBlock\",\"attrs\":{\"blockType\":\"pluginThing\",\"data\":{\"who\":\"anything\"}}}]}");
        var id = await ArticleAsync(new() { ["title"] = "x", ["body"] = body });

        (await PublishedReadAsync(Editor, id))!["body"].ShouldBeOfType<RichText>().Json.ShouldContain("anything");
        _database.References(id, Core.Storage.ContentState.Draft).ShouldBeEmpty();
    }

    [Fact]
    public async Task AnEntryOnlyInALocaleTheGrantDoesNotCoverIsNoTargetOnAnyRead()
    {
        var schema = SampleSchema.NewBuilder()
            .AddBlock("card", block => block.Field("who", "relation", field => field.Relation("author")))
            .AddCollection("author", type => type.Localized().Field("name", "text", field => field.Localizable()))
            .AddCollection("article", type => type.Field("title", "text").Field("author", "relation", field => field.Relation("author")).Field("cards", "blocks", field => field.Setting(FieldSettings.Blocks, Cards)))
            .Build();
        using var database = new ServiceDatabase(schema: schema);
        var english = WithRole(database.Database.AddRoleForTest(
            "English",
            ("content.article.read", null),
            ("content.author.read", new PermissionConstraints { Locales = ["en"] })));

        var draft = IdOf(await database.CreateAsync(Editor, new() { ["name"] = "fr" }, "author", "fr"));
        var published = IdOf(await database.CreateAsync(Editor, new() { ["name"] = "fr" }, "author", "fr"));
        await database.PublishAsync(Editor, published, "fr", type: "author", operationLocale: "fr");
        var inEnglish = IdOf(await database.CreateAsync(Editor, new() { ["name"] = "en" }, "author"));
        await database.PublishAsync(Editor, inEnglish, type: "author");

        foreach (var (target, visible) in new[] { (draft, false), (published, false), (Guid.CreateVersion7(), false), (inEnglish, true) })
        {
            var id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = Guid.NewGuid().ToString("N"), ["author"] = target, ["cards"] = new object[] { new ContentDocument { ["type"] = "card", ["who"] = target } } }, "article"));
            await database.PublishAsync(Editor, id, type: "article");

            foreach (var read in new[]
            {
                await database.GetAsync(english, id, type: "article"),
                await database.GetAsync(english, id, new GetOptions { Status = QueryStatus.Published }, "article"),
                await database.VersionAsync(english, id, 1, type: "article"),
            })
            {
                (read!["author"] is not null).ShouldBe(visible);
                Text(read["cards"]).Contains(target.ToString(), StringComparison.Ordinal).ShouldBe(visible);
            }
        }
    }

    // Whatever X is, a principal limited on the type keeps it, and publishing then says the same.
    private async Task<List<Guid>> GhostsAsync()
    {
        var vault = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "v" }, "vault"));
        var elsewhere = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "v" }, "vault", site: SiteB));
        var trashed = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "v" }, "vault"));
        await _database.DeleteAsync(Editor, trashed, "vault");
        return [vault, elsewhere, trashed, Guid.CreateVersion7()];
    }

    [Fact]
    public async Task KeepingATargetItCannotSeeSaysNothingOfWhatTheIdIs()
    {
        var limited = WithRole(_database.Database.AddRoleForTest(
            "Own authors",
            ("content.article.create", null),
            ("content.article.read", null),
            ("content.article.update", null),
            ("content.article.publish", null),
            ("content.author.read", new PermissionConstraints { RowRule = BuiltInRoles.OwnEntriesRule })));
        List<string> answers = [];

        foreach (var ghost in await GhostsAsync())
        {
            var block = IdOf(await _database.CreateAsync(limited, new() { ["title"] = "x", ["cards"] = new object[] { Card(ghost) } }, "article"));
            var read = (await _database.GetAsync(limited, block, type: "article"))!;
            await _database.UpdateAsync(limited, block, new() { ["title"] = "x", ["cards"] = read["cards"] }, 1, "article");

            var top = IdOf(await _database.CreateAsync(limited, new() { ["title"] = "x", ["author"] = ghost }, "article"));
            await _database.UpdateAsync(limited, top, new() { ["title"] = "x" }, 1, "article");

            var text = IdOf(await _database.CreateAsync(limited, new() { ["title"] = "x", ["body"] = Linked("t", ghost) }, "article"));
            var readText = (await _database.GetAsync(limited, text, type: "article"))!;
            await _database.UpdateAsync(limited, text, new() { ["title"] = "x", ["body"] = readText["body"] }, 1, "article");

            List<string> warnings = [];
            foreach (var id in new[] { block, top, text })
            {
                _database.References(id, Core.Storage.ContentState.Draft).ShouldHaveSingleItem().TargetId.ShouldBe(ghost);
                warnings.AddRange((await _database.PublishAsync(limited, id, type: "article")).Warnings.Select(warning => $"{warning.Path}|{warning.Code}|{warning.TargetId}"));
            }

            answers.Add(string.Join(';', warnings));
        }

        answers.Distinct().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task APrincipalWithAnOpenReadOfTheTypeClearsAGhostAndKeepsWhatIsThere()
    {
        var trashed = await AuthorAsync("trashed");
        foreach (var ghost in await GhostsAsync())
        {
            var block = await ArticleAsync(new() { ["title"] = Guid.NewGuid().ToString("N"), ["cards"] = new object[] { Card(ghost) } }, publish: false);
            var read = (await _database.GetAsync(Editor, block, type: "article"))!;
            await _database.UpdateAsync(Editor, block, read, 1, "article");
            _database.References(block, Core.Storage.ContentState.Draft).ShouldBeEmpty();

            var top = await ArticleAsync(new() { ["title"] = Guid.NewGuid().ToString("N"), ["author"] = ghost }, publish: false);
            await _database.UpdateAsync(Editor, top, new() { ["title"] = "cleared " + Guid.NewGuid().ToString("N") }, 1, "article");
            _database.References(top, Core.Storage.ContentState.Draft).ShouldBeEmpty();

            var text = await ArticleAsync(new() { ["title"] = Guid.NewGuid().ToString("N"), ["body"] = Linked("t", ghost) }, publish: false);
            var readText = (await _database.GetAsync(Editor, text, type: "article"))!;
            await _database.UpdateAsync(Editor, text, readText, 1, "article");
            _database.References(text, Core.Storage.ContentState.Draft).ShouldBeEmpty();
        }

        var kept = await ArticleAsync(new() { ["title"] = "trashed target", ["cards"] = new object[] { Card(trashed) } }, publish: false);
        var shown = (await _database.GetAsync(Editor, kept, type: "article"))!;
        await _database.UpdateAsync(Editor, kept, shown, 1, "article");
        _database.References(kept, Core.Storage.ContentState.Draft).ShouldHaveSingleItem().TargetId.ShouldBe(trashed);
    }

    [Fact]
    public async Task ATrashedTargetCountsAsThereForOneThatCouldNotSeeIt()
    {
        var trashed = await AuthorAsync("trashed");
        var id = await ArticleAsync(new() { ["title"] = "x", ["cards"] = new object[] { Card(trashed) } }, publish: false);
        var blindToTheEntry = WithRole(_database.Database.AddRoleForTest(
            "Blind",
            ("content.article.update", null),
            ("content.author.read", null)));

        await _database.PatchAsync(blindToTheEntry, id, new() { ["cards"] = new object[] { new ContentDocument { ["type"] = "card", ["label"] = "L" } } }, 1, "article");

        _database.References(id, Core.Storage.ContentState.Draft).ShouldHaveSingleItem().TargetId.ShouldBe(trashed);
    }

    [Fact]
    public async Task AGhostStaysRefusedUnderBlockUntilAWiderPrincipalClearsIt()
    {
        var schema = SampleSchema.NewBuilder()
            .AddCollection("author", type => type.Field("name", "text"))
            .AddCollection("page", type => type.PublishPolicy(UnpublishedReferencePolicy.Block).Field("title", "text").Field("author", "relation", field => field.Relation("author")))
            .Build();
        using var database = new ServiceDatabase(schema: schema);
        var limited = WithRole(database.Database.AddRoleForTest(
            "Own authors",
            ("content.page.create", null),
            ("content.page.read", null),
            ("content.page.update", null),
            ("content.page.publish", null),
            ("content.author.read", new PermissionConstraints { RowRule = BuiltInRoles.OwnEntriesRule })));
        var id = IdOf(await database.CreateAsync(limited, new() { ["title"] = "x", ["author"] = Guid.CreateVersion7() }, "page"));

        await database.UpdateAsync(limited, id, new() { ["title"] = "y" }, 1, "page");
        await Should.ThrowAsync<ValidationException>(() => database.PublishAsync(limited, id, type: "page"));

        await database.UpdateAsync(Editor, id, new() { ["title"] = "z" }, 2, "page");
        await Should.NotThrowAsync(() => database.PublishAsync(limited, id, type: "page"));
    }

    [Fact]
    public async Task AVersionDropsOnlyTheBlockTheSchemaHasLost()
    {
        var id = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "One" }, "article"));
        await _database.PatchAsync(Editor, id, new() { ["title"] = "Two" }, 1, "article");
        var row = _database.Versions(id)[0];
        var snapshot = JsonNode.Parse(row.Snapshot)!.AsObject();
        snapshot["cards"] = new JsonArray(new JsonObject { ["type"] = "gone", ["x"] = "ghost" }, new JsonObject { ["type"] = "card", ["label"] = "kept", ["extra"] = "ghost" });
        var text = snapshot.ToJsonString();
        _database.Read(context => context.Versions.Where(version => version.Id == row.Id).ExecuteUpdate(set => set.SetProperty(version => version.Snapshot, text)));

        var version = (await _database.VersionAsync(Editor, id, 1, type: "article"))!;
        var card = ((JsonArray)version["cards"]!).ShouldHaveSingleItem()!;
        card["label"]!.GetValue<string>().ShouldBe("kept");
        Text(version).ShouldNotContain("ghost");

        var restored = await _database.RestoreVersionAsync(Editor, id, 1, type: "article");
        ((JsonArray)restored["cards"]!).ShouldHaveSingleItem()!["label"]!.GetValue<string>().ShouldBe("kept");
    }
}
