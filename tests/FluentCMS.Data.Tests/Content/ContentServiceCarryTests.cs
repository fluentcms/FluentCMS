using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Core.Tests.Schema.Samples;
using FluentCMS.Schema;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

// What a client could not see surviving what it sends back.
public sealed class ContentServiceCarryTests : IDisposable
{
    private readonly ServiceDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private static ContentDocument Titled(string title) => new() { ["title"] = title };

    private Task<ContentDocument> SystemPatchAsync(Guid id, ContentDocument patch, int version) =>
        _database.AsAsync(Principal.System, services => services.GetRequiredService<IContentService>().PatchAsync("article", id, patch, version, Token), origin: Origin.Job);

    private JsonNode SectionsOf(Guid id) => JsonNode.Parse((string)_database.Row("cms_article", id)["sections"])!;

    [Fact]
    public async Task AnAuthorSendingBackWhatItReadKeepsTargetsItCannotRead()
    {
        var other = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "Not mine" }, "author"));
        var tag = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "Global" }, "tag"));
        var ann = AuthorRole(Guid.CreateVersion7());
        var own = IdOf(await _database.CreateAsync(ann, new() { ["name"] = "Mine" }, "author"));
        var entry = await _database.CreateAsync(ann, new() { ["title"] = "Mine", ["author"] = other, ["sponsor"] = own, ["tags"] = new[] { tag } });
        var id = IdOf(entry);

        var read = (await _database.GetAsync(ann, id))!;
        read["author"].ShouldBeNull();
        ((List<ContentDocument>)read["tags"]!).ShouldBeEmpty();
        read["title"] = "Still mine";
        await _database.UpdateAsync(ann, id, read, 1);

        var editorSees = (await _database.GetAsync(Editor, id))!;
        ((ContentDocument)editorSees["author"]!)["id"].ShouldBe(other);
        ((ContentDocument)editorSees["sponsor"]!)["id"].ShouldBe(own);
        ((List<ContentDocument>)editorSees["tags"]!).ShouldHaveSingleItem()["id"].ShouldBe(tag);
        editorSees["title"].ShouldBe("Still mine");
    }

    [Fact]
    public async Task AMultipleRelationKeepsUnreadableTargetsAlongsideWhatWasSent()
    {
        var role = _database.Database.AddRoleForTest(
            "Own tags",
            ("content.article.read", null),
            ("content.article.update", null),
            ("content.tag.read", new PermissionConstraints { RowRule = BuiltInRoles.OwnEntriesRule }),
            ("content.tag.create", null));
        var principal = WithRole(role, Guid.CreateVersion7());
        var hiddenTag = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "Theirs" }, "tag"));
        var ownTag = IdOf(await _database.CreateAsync(principal, new() { ["name"] = "Mine" }, "tag"));
        var newTag = IdOf(await _database.CreateAsync(principal, new() { ["name"] = "New" }, "tag"));
        var entry = await _database.CreateAsync(Editor, new() { ["title"] = "T", ["tags"] = new[] { hiddenTag, ownTag } });
        var id = IdOf(entry);

        (await _database.GetAsync(principal, id))!["tags"].ShouldBeOfType<List<ContentDocument>>().ShouldHaveSingleItem()["id"].ShouldBe(ownTag);

        await _database.UpdateAsync(principal, id, new() { ["title"] = "T", ["tags"] = new[] { newTag } }, 1);

        _database.References(id).Where(row => row.Field == "tags").Select(row => row.TargetId).ShouldBe([newTag, hiddenTag]);
    }

    [Fact]
    public async Task APatchNullClearsARelationEvenWhenItsTargetIsUnreadable()
    {
        var role = _database.Database.AddRoleForTest("Articles", ("content.article.read", null), ("content.article.update", null));
        var ann = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "Ann" }, "author"));
        var entry = await _database.CreateAsync(Editor, new() { ["title"] = "T", ["author"] = ann });

        await _database.PatchAsync(WithRole(role), IdOf(entry), new() { ["author"] = null }, 1);

        _database.References(IdOf(entry)).ShouldBeEmpty();
    }

    [Fact]
    public async Task AReadableTargetLeftOutOfAFullUpdateIsCleared()
    {
        var ann = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "Ann" }, "author"));
        var entry = await _database.CreateAsync(Editor, new() { ["title"] = "T", ["author"] = ann });

        await _database.UpdateAsync(Editor, IdOf(entry), Titled("T"), 1);

        _database.References(IdOf(entry)).ShouldBeEmpty();
    }

    public static TheoryData<bool, ContentDocument> GroupsSentWithoutTheHiddenValue() => new()
    {
        { false, new() { ["title"] = "T" } },
        { false, new() { ["title"] = "T", ["seo"] = null } },
        { false, new() { ["title"] = "T", ["seo"] = new ContentDocument() } },
        { false, new() { ["title"] = "T", ["seo"] = new ContentDocument { ["metaTitle"] = null } } },
        { false, new() { ["title"] = "T", ["seo"] = new ContentDocument { ["metaTitle"] = "x" } } },
        { true, new() { ["seo"] = null } },
        { true, new() { ["seo"] = new ContentDocument { ["metaTitle"] = null, ["image"] = null } } },
    };

    [Theory]
    [MemberData(nameof(GroupsSentWithoutTheHiddenValue))]
    public async Task AHiddenValueInAComponentSurvivesWhateverAClientSends(bool isPatch, ContentDocument change)
    {
        var entry = await _database.CreateAsync(Editor, new() { ["title"] = "T", ["seo"] = new ContentDocument { ["metaTitle"] = "m" } });
        var id = IdOf(entry);
        await SystemPatchAsync(id, new() { ["seo"] = new ContentDocument { ["internal"] = "kept" } }, 1);

        if (isPatch)
        {
            await _database.PatchAsync(Editor, id, change, 2);
        }
        else
        {
            await _database.UpdateAsync(Editor, id, change, 2);
        }

        _database.Row("cms_article", id)["seo_internal"].ShouldBe("kept");
        JsonNode.Parse(_database.Versions(id).Last().Snapshot)!["seo"]!["internal"]!.GetValue<string>().ShouldBe("kept");
    }

    [Fact]
    public async Task TheSystemPrincipalMayStillClearAHiddenValueInAComponent()
    {
        var entry = await _database.CreateAsync(Editor, Titled("T"));
        await SystemPatchAsync(IdOf(entry), new() { ["seo"] = new ContentDocument { ["internal"] = "kept" } }, 1);

        await SystemPatchAsync(IdOf(entry), new() { ["seo"] = new ContentDocument { ["internal"] = null } }, 2);

        _database.Row("cms_article", IdOf(entry))["seo_internal"].ShouldBeNull();
    }

    [Fact]
    public async Task AHiddenValueInAComponentInsideABlockSurvivesTheComponentBeingLeftOut()
    {
        var entry = await _database.CreateAsync(Editor, Titled("T"));
        var id = IdOf(entry);
        await SystemPatchAsync(id, new() { ["sections"] = JsonNode.Parse("""[{"type":"hero","heading":"A","internal":"I","details":{"metaTitle":"d","internal":"NESTED"}}]""") }, 1);

        await _database.PatchAsync(Editor, id, new() { ["sections"] = JsonNode.Parse("""[{"type":"hero","heading":"A2"}]""") }, 2);

        var block = SectionsOf(id)[0]!;
        block["heading"]!.GetValue<string>().ShouldBe("A2");
        block["internal"]!.GetValue<string>().ShouldBe("I");
        block["details"]!["internal"]!.GetValue<string>().ShouldBe("NESTED");
    }

    private async Task<Guid> TwoBlocksAsync()
    {
        var entry = await _database.CreateAsync(Editor, Titled("T"));
        await SystemPatchAsync(IdOf(entry), new() { ["sections"] = JsonNode.Parse("""[{"type":"hero","heading":"A","internal":"A-approved"},{"type":"hero","heading":"B","internal":"B-rejected"}]""") }, 1);
        return IdOf(entry);
    }

    [Fact]
    public async Task HiddenValuesFollowBlocksThatMove()
    {
        var id = await TwoBlocksAsync();

        await _database.PatchAsync(Editor, id, new() { ["sections"] = JsonNode.Parse("""[{"type":"hero","heading":"B"},{"type":"hero","heading":"A"}]""") }, 2);

        var sections = SectionsOf(id);
        sections[0]!["heading"]!.GetValue<string>().ShouldBe("B");
        sections[0]!["internal"]!.GetValue<string>().ShouldBe("B-rejected");
        sections[1]!["internal"]!.GetValue<string>().ShouldBe("A-approved");
    }

    [Fact]
    public async Task ARemovedBlockTakesItsHiddenValueWithIt()
    {
        var id = await TwoBlocksAsync();

        await _database.PatchAsync(Editor, id, new() { ["sections"] = JsonNode.Parse("""[{"type":"hero","heading":"B"}]""") }, 2);

        SectionsOf(id)[0]!["internal"]!.GetValue<string>().ShouldBe("B-rejected");
    }

    [Fact]
    public async Task AnEditedBlockFallsBackToItsPlaceAmongTheBlocksThatDidNotMatch()
    {
        var id = await TwoBlocksAsync();

        await _database.PatchAsync(Editor, id, new() { ["sections"] = JsonNode.Parse("""[{"type":"hero","heading":"A"},{"type":"hero","heading":"B2"}]""") }, 2);

        SectionsOf(id)[1]!["internal"]!.GetValue<string>().ShouldBe("B-rejected");
    }

    [Fact]
    public async Task TheSameBlockTwiceUsesEachStoredOneOnce()
    {
        var entry = await _database.CreateAsync(Editor, Titled("T"));
        await SystemPatchAsync(IdOf(entry), new() { ["sections"] = JsonNode.Parse("""[{"type":"hero","heading":"A","internal":"first"},{"type":"hero","heading":"A","internal":"second"}]""") }, 1);

        await _database.PatchAsync(Editor, IdOf(entry), new() { ["sections"] = JsonNode.Parse("""[{"type":"hero","heading":"A"},{"type":"hero","heading":"A"}]""") }, 2);

        var sections = SectionsOf(IdOf(entry));
        sections[0]!["internal"]!.GetValue<string>().ShouldBe("first");
        sections[1]!["internal"]!.GetValue<string>().ShouldBe("second");
    }

    [Fact]
    public async Task ABlockOfAnotherTypeDoesNotInheritAHiddenValue()
    {
        var id = await TwoBlocksAsync();

        await _database.PatchAsync(Editor, id, new() { ["sections"] = JsonNode.Parse("""[{"type":"quote","text":"A"},{"type":"hero","heading":"B"}]""") }, 2);

        var sections = SectionsOf(id);
        sections[0]!["internal"].ShouldBeNull();
        sections[1]!["internal"]!.GetValue<string>().ShouldBe("B-rejected");
    }

    [Fact]
    public async Task ATypedRoundTripKeepsWhatThePrincipalCouldNotRead()
    {
        using var database = new ServiceDatabase(schema: SampleSchema.FromClasses().Build());
        var created = await database.AsAsync(Editor, services => services.GetRequiredService<IContentService<Article>>().CreateAsync(new Article { Title = "T", Category = ArticleCategory.Video, VideoUrl = "https://example.com/v" }, Token));
        var role = database.Database.AddRoleForTest("Titles", ("content.article.read", new PermissionConstraints { Fields = ["title"] }), ("content.article.update", null));

        await database.AsAsync(WithRole(role), async services =>
        {
            var service = services.GetRequiredService<IContentService<Article>>();
            var read = (await service.GetAsync(created.Id, new GetOptions { Status = QueryStatus.Draft }, Token))!;
            read.Category.ShouldBe(default);
            read.Title = "Changed";
            return await service.UpdateAsync(created.Id, read, read.Version, Token);
        });

        var stored = (await database.AsAsync(Editor, services => services.GetRequiredService<IContentService<Article>>().GetAsync(created.Id, new GetOptions { Status = QueryStatus.Draft }, Token)))!;
        stored.Title.ShouldBe("Changed");
        stored.Category.ShouldBe(ArticleCategory.Video);
        stored.VideoUrl.ShouldBe("https://example.com/v");
    }

    [FluentCMS.Schema.Collection("scored")]
    public sealed class Scored : ContentItem
    {
        [Text]
        public string Name { get; set; } = "";

        [Integer, Hidden]
        public int Score { get; set; }

        [Integer, FluentCMS.Schema.ReadOnly]
        public int Stamp { get; set; }
    }

    [Fact]
    public async Task ATypedWriteLeavesHiddenAndReadOnlyPropertiesOut()
    {
        using var database = new ServiceDatabase(schema: SampleSchema.NewBuilder().AddContentType<Scored>().Build());

        var created = await database.AsAsync(Editor, services => services.GetRequiredService<IContentService<Scored>>().CreateAsync(new Scored { Name = "n", Score = 7, Stamp = 8 }, Token));

        created.Name.ShouldBe("n");
        var row = database.Row("cms_scored", created.Id);
        row["score"].ShouldBeNull();
        row["stamp"].ShouldBeNull();

        created.Name = "m";
        var updated = await database.AsAsync(Editor, services => services.GetRequiredService<IContentService<Scored>>().UpdateAsync(created.Id, created, created.Version, Token));
        updated.Name.ShouldBe("m");
    }

    [Fact]
    public async Task ATypedHookStillFillsAHiddenProperty()
    {
        using var database = new ServiceDatabase(
            cms => cms.Services.AddSingleton<FluentCMS.Hooks.IBeforeCreateHook<Scored>, ScoreIt>(),
            SampleSchema.NewBuilder().AddContentType<Scored>().Build());

        var created = await database.AsAsync(Editor, services => services.GetRequiredService<IContentService<Scored>>().CreateAsync(new Scored { Name = "n" }, Token));

        database.Row("cms_scored", created.Id)["score"].ShouldBe(9L);
    }

    private sealed class ScoreIt : FluentCMS.Hooks.IBeforeCreateHook<Scored>
    {
        public ValueTask BeforeCreateAsync(FluentCMS.Hooks.HookContext<Scored> context, CancellationToken cancellationToken)
        {
            context.Entry.Score = 9;
            return ValueTask.CompletedTask;
        }
    }
}
