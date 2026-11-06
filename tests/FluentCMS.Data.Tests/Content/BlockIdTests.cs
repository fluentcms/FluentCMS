using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Schema;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

// Blocks and inline blocks carry a stable id, and a write pairs each block it is sent with its stored self by that id, so
// hidden and read-only values stay with their own block (#2751).
public sealed partial class BlockIdTests : IDisposable
{
    private static readonly string[] Cards = ["card"];
    private static readonly string[] TopLevel = ["card", "panel"];

    private static SchemaModel Model() => SampleSchema.NewBuilder()
        .AddBlock("card", block => block
            .Field("label", "text")
            .Field("stamp", "text", field => field.IsReadOnly())
            .Field("internal", "text", field => field.Hidden()))
        .AddBlock("wrap", block => block.Field("text", "richText"))
        .AddBlock("panel", block => block.Field("items", "blocks", field => field.Setting(FieldSettings.Blocks, Cards)))
        .AddCollection("article", type => type
            .Field("title", "text")
            .Field("cards", "blocks", field => field.Setting(FieldSettings.Blocks, TopLevel))
            .Field("body", "richText"))
        .Build();

    private readonly ServiceDatabase _database = new(schema: Model());

    public void Dispose() => _database.Dispose();

    // Only the keys given, since a key a principal may not write is refused even when it holds null.
    private static string Card(string label, string? stamp = null, string? @internal = null, string? id = null)
    {
        JsonObject card = new() { ["type"] = "card" };
        foreach (var (key, value) in new[] { ("id", id), ("label", label), ("stamp", stamp), ("internal", @internal) })
        {
            if (value is not null)
            {
                card[key] = value;
            }
        }

        return card.ToJsonString();
    }

    private static JsonNode List(params string[] blocks) => JsonNode.Parse("[" + string.Join(",", blocks) + "]")!;

    private static string Inline(string block, string data, string? id = null)
    {
        JsonObject attributes = new() { ["blockType"] = block, ["data"] = JsonNode.Parse(data) };
        if (id is not null)
        {
            attributes["id"] = id;
        }

        return new JsonObject { ["type"] = "inlineBlock", ["attrs"] = attributes }.ToJsonString();
    }

    private static string Document(params string[] inlines) =>
        """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Before"},""" + string.Join(",", inlines) + "]}]}";

    private async Task<Guid> SystemCreateAsync(ContentDocument entry) =>
        IdOf(await _database.AsAsync(Principal.System, services => services.GetRequiredService<IContentService>().CreateAsync("article", entry, Token), origin: Origin.Seed));

    private JsonNode Stored(Guid id, string field) => JsonNode.Parse((string)_database.Row("cms_article", id)[field])!;

    private static JsonNode Json(object? value) => value is RichText text ? JsonNode.Parse(text.Json)! : JsonNode.Parse(JsonSerializer.Serialize(value))!;

    [GeneratedRegex("^[A-Za-z0-9_-]{12}$")]
    private static partial Regex AssignedId();

    [Fact]
    public async Task EveryBlockGetsAnIdThatAReadReturns()
    {
        var id = await SystemCreateAsync(new()
        {
            ["title"] = "T",
            ["cards"] = List(Card("A"), """{"type":"panel","items":[""" + Card("B") + "]}"),
            ["body"] = new RichText(Document(Inline("card", """{"label":"C"}"""))),
        });

        var cards = Stored(id, "cards");
        string[] ids = [(string)cards[0]!["id"]!, (string)cards[1]!["id"]!, (string)cards[1]!["items"]![0]!["id"]!];
        ids.ShouldAllBe(value => AssignedId().IsMatch(value));
        ids.Distinct().Count().ShouldBe(3);
        cards[0]!.AsObject().Select(pair => pair.Key).Take(2).ShouldBe(["type", "id"]);
        var inline = (string)Stored(id, "body")["content"]![0]!["content"]![1]!["attrs"]!["id"]!;
        AssignedId().IsMatch(inline).ShouldBeTrue();

        var read = (await _database.GetAsync(Editor, id))!;
        JsonNode.DeepEquals(Json(read["cards"]), cards).ShouldBeTrue();
        Json(read["body"])["content"]![0]!["content"]![1]!["attrs"]!["id"]!.GetValue<string>().ShouldBe(inline);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemovingOneBlockAndEditingTheNextKeepsEachHiddenValueWithItsOwnBlock(bool isPatch)
    {
        var id = await SystemCreateAsync(new() { ["title"] = "T", ["cards"] = List(Card("A", "ONE", "approved"), Card("B", "TWO", "rejected")) });
        var before = Stored(id, "cards");

        var cards = (JsonArray)Json((await _database.GetAsync(Editor, id))!["cards"]);
        cards.RemoveAt(0);
        cards[0]!["label"] = "B2";
        if (isPatch)
        {
            await _database.PatchAsync(Editor, id, new() { ["cards"] = cards }, 1);
        }
        else
        {
            await _database.UpdateAsync(Editor, id, new() { ["title"] = "T", ["cards"] = cards }, 1);
        }

        var card = Stored(id, "cards").AsArray().ShouldHaveSingleItem()!;
        card["label"]!.GetValue<string>().ShouldBe("B2");
        card["internal"]!.GetValue<string>().ShouldBe("rejected");
        card["stamp"]!.GetValue<string>().ShouldBe("TWO");
        card["id"]!.GetValue<string>().ShouldBe(before[1]!["id"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OfTwoIdenticalBlocksTheOneKeptKeepsItsOwnValues(bool sendsReadOnlyValue)
    {
        var id = await SystemCreateAsync(new() { ["title"] = "T", ["cards"] = List(Card("Same", "ONE", "first"), Card("Same", "TWO", "second")) });

        var cards = (JsonArray)Json((await _database.GetAsync(Editor, id))!["cards"]);
        cards.RemoveAt(0);
        if (!sendsReadOnlyValue)
        {
            cards[0]!.AsObject().Remove("stamp");
        }

        await _database.PatchAsync(Editor, id, new() { ["cards"] = cards }, 1);

        var card = Stored(id, "cards").AsArray().ShouldHaveSingleItem()!;
        card["stamp"]!.GetValue<string>().ShouldBe("TWO");
        card["internal"]!.GetValue<string>().ShouldBe("second");
    }

    [Fact]
    public async Task ABlockMovedIntoAndOutOfTheDocumentOfAnInlineBlockKeepsItsValues()
    {
        var id = await SystemCreateAsync(new()
        {
            ["title"] = "T",
            ["body"] = new RichText(Document(
                Inline("card", """{"label":"C","stamp":"STAMPED","internal":"SECRET"}"""),
                Inline("wrap", "{\"text\":" + """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Inside"}]}]}""" + "}"))),
        });
        var cardId = (string)Stored(id, "body")["content"]![0]!["content"]![1]!["attrs"]!["id"]!;

        // In: the card goes from the document into the paragraph of the wrap's own document.
        var body = Json((await _database.GetAsync(Editor, id))!["body"]);
        var top = body["content"]![0]!["content"]!.AsArray();
        var card = top[1]!;
        top.RemoveAt(1);
        top[1]!["attrs"]!["data"]!["text"]!["content"]![0]!["content"]!.AsArray().Add(card);
        await _database.PatchAsync(Editor, id, new() { ["body"] = new RichText(body.ToJsonString()) }, 1);

        var moved = Stored(id, "body")["content"]![0]!["content"]![1]!["attrs"]!["data"]!["text"]!["content"]![0]!["content"]![1]!["attrs"]!;
        moved["id"]!.GetValue<string>().ShouldBe(cardId);
        moved["data"]!["stamp"]!.GetValue<string>().ShouldBe("STAMPED");
        moved["data"]!["internal"]!.GetValue<string>().ShouldBe("SECRET");

        // Out: and back to the end of the document.
        body = Json((await _database.GetAsync(Editor, id))!["body"]);
        var inner = body["content"]![0]!["content"]![1]!["attrs"]!["data"]!["text"]!["content"]![0]!["content"]!.AsArray();
        card = inner[1]!;
        inner.RemoveAt(1);
        body["content"]![0]!["content"]!.AsArray().Add(card);
        await _database.PatchAsync(Editor, id, new() { ["body"] = new RichText(body.ToJsonString()) }, 2);

        var back = Stored(id, "body")["content"]![0]!["content"]![2]!["attrs"]!;
        back["id"]!.GetValue<string>().ShouldBe(cardId);
        back["data"]!["stamp"]!.GetValue<string>().ShouldBe("STAMPED");
        back["data"]!["internal"]!.GetValue<string>().ShouldBe("SECRET");
    }

    [Fact]
    public async Task ABlockMovedIntoANestedListKeepsItsValues()
    {
        var id = await SystemCreateAsync(new() { ["title"] = "T", ["cards"] = List(Card("A", "STAMPED", "SECRET"), """{"type":"panel","items":[]}""") });

        var cards = (JsonArray)Json((await _database.GetAsync(Editor, id))!["cards"]);
        var card = cards[0]!;
        cards.RemoveAt(0);
        cards[0]!["items"]!.AsArray().Add(card);
        await _database.UpdateAsync(Editor, id, new() { ["title"] = "T", ["cards"] = cards }, 1);

        var moved = Stored(id, "cards")[0]!["items"]![0]!;
        moved["stamp"]!.GetValue<string>().ShouldBe("STAMPED");
        moved["internal"]!.GetValue<string>().ShouldBe("SECRET");
    }

    public static TheoryData<string, string, string> Duplicates() => new()
    {
        { "cards", "[" + Card("A", id: "same") + "," + Card("B", id: "same") + "]", "cards[1].id" },
        { "cards", "[" + Card("A", id: "same") + """,{"type":"panel","items":[""" + Card("B", id: "same") + "]}]", "cards[1].items[0].id" },
        { "body", Document(Inline("card", """{"label":"A"}""", "same"), Inline("card", """{"label":"B"}""", "same")), "body.content[0].content[2].attrs.id" },
    };

    [Theory]
    [MemberData(nameof(Duplicates))]
    public async Task AnIdSentTwiceInAFieldIsRefused(string field, string json, string path)
    {
        var id = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "T" }));
        object value = field == "body" ? new RichText(json) : JsonNode.Parse(json)!;

        var error = (await Should.ThrowAsync<ValidationException>(() => _database.PatchAsync(Editor, id, new() { [field] = value }, 1))).Errors.ShouldHaveSingleItem();

        error.Path.ShouldBe(path);
        error.Code.ShouldBe(ContentErrorCodes.DuplicateBlockId);
        _database.Versions(id).Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("""[{"type":"card","id":"has space","label":"A"}]""", "cards[0].id")]
    [InlineData("""[{"type":"card","id":7,"label":"A"}]""", "cards[0].id")]
    [InlineData("""[{"type":"card","id":"","label":"A"}]""", "cards[0].id")]
    public async Task AnIdThatIsNotOneToSixtyFourUrlSafeCharactersIsRefused(string cards, string path)
    {
        var error = (await Should.ThrowAsync<ValidationException>(() => _database.CreateAsync(Editor, new() { ["title"] = "T", ["cards"] = JsonNode.Parse(cards) }))).Errors.ShouldHaveSingleItem();

        error.Path.ShouldBe(path);
        error.Code.ShouldBe(ValidationErrorCodes.InvalidValue);
    }

    [Fact]
    public async Task AnInlineBlockIdThatIsNotUrlSafeIsRefused()
    {
        var errors = (await Should.ThrowAsync<ValidationException>(() => _database.CreateAsync(Editor, new()
        {
            ["title"] = "T",
            ["body"] = new RichText(Document(Inline("card", """{"label":"A"}""", "has space"))),
        }))).Errors;

        errors.ShouldHaveSingleItem().Path.ShouldStartWith("body");
        errors[0].Message.ShouldContain("block id");
    }

    [Fact]
    public async Task AnImportedIdIsKept()
    {
        var long64 = new string('a', 63) + "-";
        var id = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "T", ["cards"] = List(Card("A", id: long64), Card("B", id: "x")) }));

        var cards = Stored(id, "cards");
        cards[0]!["id"]!.GetValue<string>().ShouldBe(long64);
        cards[1]!["id"]!.GetValue<string>().ShouldBe("x");
    }

    [Fact]
    public async Task ABlockWithAnIdThatIsNotStoredIsNewAndCarriesNoHiddenValue()
    {
        var id = await SystemCreateAsync(new() { ["title"] = "T", ["cards"] = List(Card("A", "STAMPED", "SECRET")) });

        await _database.PatchAsync(Editor, id, new() { ["cards"] = List(Card("A", id: "imported-1")) }, 1);

        var card = Stored(id, "cards").AsArray().ShouldHaveSingleItem()!;
        card["id"]!.GetValue<string>().ShouldBe("imported-1");
        card["internal"].ShouldBeNull();
        card["stamp"].ShouldBeNull();
    }

    [Fact]
    public async Task AReadOnlyValueSentOnABlockWithAnIdThatIsNotStoredIsRefused()
    {
        var id = await SystemCreateAsync(new() { ["title"] = "T", ["cards"] = List(Card("A", "STAMPED", "SECRET")) });

        var error = (await Should.ThrowAsync<ValidationException>(() =>
            _database.PatchAsync(Editor, id, new() { ["cards"] = List(Card("A", "STAMPED", id: "imported-1")) }, 1))).Errors.ShouldHaveSingleItem();

        error.Path.ShouldBe("cards.stamp");
        error.Code.ShouldBe(AuthorizationErrorCodes.FieldNotWritable);
    }

    [Fact]
    public async Task ABlockSentWithoutItsIdPairsAsBeforeAndKeepsTheStoredId()
    {
        var id = await SystemCreateAsync(new() { ["title"] = "T", ["cards"] = List(Card("A", "ONE", "first"), Card("B", "TWO", "second")) });
        var before = Stored(id, "cards");

        await _database.PatchAsync(Editor, id, new() { ["cards"] = List(Card("B"), Card("A")) }, 1);

        var after = Stored(id, "cards");
        after[0]!["internal"]!.GetValue<string>().ShouldBe("second");
        after[0]!["id"]!.GetValue<string>().ShouldBe(before[1]!["id"]!.GetValue<string>());
        after[1]!["id"]!.GetValue<string>().ShouldBe(before[0]!["id"]!.GetValue<string>());
    }

    [FluentCMS.Schema.Collection("page")]
    public sealed class Page : ContentItem
    {
        [Text]
        public string Title { get; set; } = "";

        [Blocks(typeof(Tile), typeof(Plain))]
        public BlockList Sections { get; set; } = [];
    }

    [Block("tile")]
    public sealed class Tile
    {
        public string? Id { get; set; }

        [Text]
        public string Label { get; set; } = "";

        [Text, Hidden]
        public string? Internal { get; set; }
    }

    [Block("plain")]
    public sealed class Plain
    {
        [Text]
        public string Label { get; set; } = "";

        [Text, Hidden]
        public string? Internal { get; set; }
    }

    [Fact]
    public async Task ATypedBlockListKeepsIdsWithAndWithoutAnIdProperty()
    {
        using var database = new ServiceDatabase(schema: SampleSchema.NewBuilder().AddContentType<Page>().Build());
        var created = await database.AsAsync(Principal.System, services => services.GetRequiredService<IContentService<Page>>().CreateAsync(
            new Page { Title = "T", Sections = [new Tile { Label = "A", Internal = "tile-secret" }, new Plain { Label = "B", Internal = "plain-secret" }] }, Token), origin: Origin.Seed);
        var stored = JsonNode.Parse((string)database.Row("cms_page", created.Id)["sections"])!;
        var tileId = stored[0]!["id"]!.GetValue<string>();
        var plainId = stored[1]!["id"]!.GetValue<string>();

        var read = (await database.AsAsync(Editor, services => services.GetRequiredService<IContentService<Page>>().GetAsync(created.Id, new GetOptions { Status = QueryStatus.Draft }, Token)))!;
        read.Sections[0].ShouldBeOfType<Tile>().Id.ShouldBe(tileId);

        read.Sections = [read.Sections[1], read.Sections[0]];
        ((Plain)read.Sections[0]).Label = "B2";
        ((Tile)read.Sections[1]).Label = "A2";
        await database.AsAsync(Editor, services => services.GetRequiredService<IContentService<Page>>().UpdateAsync(created.Id, read, read.Version, Token));

        var after = JsonNode.Parse((string)database.Row("cms_page", created.Id)["sections"])!;
        after[0]!["label"]!.GetValue<string>().ShouldBe("B2");
        after[0]!["id"]!.GetValue<string>().ShouldBe(plainId);
        after[0]!["internal"]!.GetValue<string>().ShouldBe("plain-secret");
        after[1]!["id"]!.GetValue<string>().ShouldBe(tileId);
        after[1]!["internal"]!.GetValue<string>().ShouldBe("tile-secret");
    }
}
