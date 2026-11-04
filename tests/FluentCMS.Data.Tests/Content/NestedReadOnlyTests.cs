using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

// A read-only value inside a component, a block, the data of an inline block, or an inline block inside a block, that
// a write sends back as the read showed it at the same place. Each case runs on every place.
public sealed class NestedReadOnlyTests : IDisposable
{
    private static readonly string[] Cards = ["card", "story"];

    private static SchemaModel Model() => SampleSchema.NewBuilder()
        .AddComponent("seo", component => component
            .Field("label", "text")
            .Field("stamp", "text", field => field.IsReadOnly())
            .Field("internal", "text", field => field.Hidden()))
        .AddBlock("card", block => block
            .Field("label", "text")
            .Field("stamp", "text", field => field.IsReadOnly())
            .Field("internal", "text", field => field.Hidden()))
        .AddBlock("story", block => block.Field("text", "richText"))
        .AddCollection("article", type => type
            .Localized()
            .Field("title", "text", field => field.Localizable())
            .Field("seo", "component", field => field.Setting(FieldSettings.Component, "seo"))
            .Field("list", "component", field => field.Multiple().Setting(FieldSettings.Component, "seo"))
            .Field("cards", "blocks", field => field.Setting(FieldSettings.Blocks, Cards))
            .Field("body", "richText", field => field.Localizable()))
        .Build();

    private readonly ServiceDatabase _database = new(schema: Model());

    public void Dispose() => _database.Dispose();

    private static readonly string[] AllPlaces = ["seo", "list", "cards", "body", "nested"];

    private static readonly string[] Stamps = ["ONE", "TWO"];

    public static TheoryData<string> Places() => new(AllPlaces);

    public static TheoryData<string> Lists() => new(AllPlaces.Where(place => place != "seo"));

    public static TheoryData<string, bool> PlacesAndWrites()
    {
        var data = new TheoryData<string, bool>();
        foreach (var place in AllPlaces)
        {
            data.Add(place, false);
            data.Add(place, true);
        }

        return data;
    }

    private static string Field(string place) => place == "nested" ? "cards" : place;

    private static string StampPath(string place) => place == "nested" ? "cards.text.stamp" : $"{place}.stamp";

    private static string Document(IEnumerable<string> cards) =>
        """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Before"},"""
        + string.Join(",", cards.Select(card => $$$"""{"type":"inlineBlock","attrs":{"blockType":"card","data":{{{card}}}}}""")) + "]}]}";

    // The cards, each the data of one as JSON, at a place: the single component takes the first.
    private static object? Value(string place, params string[] cards) => place switch
    {
        "seo" => JsonNode.Parse(cards[0]),
        "list" => JsonNode.Parse("[" + string.Join(",", cards) + "]"),
        "cards" => JsonNode.Parse("[" + string.Join(",", cards.Select(card => """{"type":"card",""" + card[1..])) + "]"),
        "body" => new RichText(Document(cards)),
        _ => JsonNode.Parse("""[{"type":"story","text":""" + Document(cards) + "}]"),
    };

    private Task<ContentDocument> SystemAsync(Func<IContentService, Task<ContentDocument>> work) =>
        _database.AsAsync(Principal.System, services => work(services.GetRequiredService<IContentService>()), origin: Origin.Seed);

    private async Task<Guid> SystemCreateAsync(string place, params string[] cards) =>
        IdOf(await SystemAsync(service => service.CreateAsync("article", new() { ["title"] = "T", [Field(place)] = Value(place, cards) }, Token)));

    private string Stored(Guid id) => JsonSerializer.Serialize(_database.Row("cms_article", id)) + JsonSerializer.Serialize(_database.Row("cms_article_i18n", id, "en"));

    // A value as a read gave it, with one text changed, in the form the read gave it.
    private static object? Edited(object? read, string from, string to) => read switch
    {
        RichText document => new RichText(document.Json.Replace($"\"{from}\"", $"\"{to}\"", StringComparison.Ordinal)),
        JsonNode node => JsonNode.Parse(node.ToJsonString().Replace($"\"{from}\"", $"\"{to}\"", StringComparison.Ordinal)),
        ContentDocument group => new ContentDocument(group.Select(pair => KeyValuePair.Create(pair.Key, Equals(pair.Value, from) ? to : pair.Value))),
        _ => throw new InvalidOperationException($"Unexpected {read?.GetType()}"),
    };

    [Theory]
    [MemberData(nameof(PlacesAndWrites))]
    public async Task AReadOnlyValueSentBackAsReadIsNoWrite(string place, bool isPatch)
    {
        var id = await SystemCreateAsync(place, """{"label":"ALPHA","stamp":"STAMPED","internal":"SECRET"}""");
        var read = (await _database.GetAsync(Editor, id))!;
        JsonSerializer.Serialize(read).ShouldContain("STAMPED");

        read[Field(place)] = Edited(read[Field(place)], "ALPHA", "BRAVO");
        if (isPatch)
        {
            await _database.PatchAsync(Editor, id, new() { [Field(place)] = read[Field(place)] }, 1);
        }
        else
        {
            await _database.UpdateAsync(Editor, id, read, 1);
        }

        var stored = Stored(id);
        stored.ShouldContain("BRAVO");
        stored.ShouldNotContain("ALPHA");
        stored.ShouldContain("STAMPED");
        stored.ShouldContain("SECRET");
    }

    [Theory]
    [MemberData(nameof(Places))]
    public async Task AReadOnlyValueSentBackAsJsonIsNoWrite(string place)
    {
        var id = await SystemCreateAsync(place, """{"label":"A","stamp":"STAMPED","internal":"SECRET"}""");
        var read = (await _database.GetAsync(Editor, id))!;
        var json = read[Field(place)] is RichText text ? text.Json : JsonSerializer.Serialize(read[Field(place)]);
        using var sent = JsonDocument.Parse(json);

        await _database.PatchAsync(Editor, id, new() { [Field(place)] = sent.RootElement }, 1);

        Stored(id).ShouldContain("STAMPED");
        Stored(id).ShouldContain("SECRET");
    }

    public static TheoryData<string, string[], string[]> Changes()
    {
        var data = new TheoryData<string, string[], string[]>();
        foreach (var place in AllPlaces)
        {
            data.Add(place, [Card("A", "STAMPED")], [Card("A", "FORGED")]);
            data.Add(place, [Card("A", "STAMPED")], ["""{"label":"A","stamp":null}"""]);
            data.Add(place, ["""{"label":"A"}"""], [Card("A", "STAMPED")]);
            if (place != "seo")
            {
                data.Add(place, [Card("A", "STAMPED")], [Card("A", "STAMPED"), Card("C", "STAMPED")]);
                data.Add(place, [Card("A", "ONE"), Card("B", "TWO")], [Card("B", "ONE"), Card("A", "TWO")]);
                data.Add(place, [Card("A", "ONE"), Card("B", "TWO")], [Card("N", "ONE"), """{"label":"A"}""", """{"label":"B"}"""]);
                data.Add(place, [Card("A", "ONE"), Card("B", "TWO")], [Card("B", "ONE")]);
                data.Add(place, ["""{"label":"A"}""", Card("B", "TWO")], ["""{"label":"B"}""", Card("A", "TWO")]);
                data.Add(place, [Card("A", "ONE"), Card("B", "TWO")], [Card("A", "ONE"), Card("B", "ONE")]);
            }
        }

        return data;
    }

    private static string Card(string label, string stamp) => $$"""{"label":"{{label}}","stamp":"{{stamp}}"}""";

    [Theory]
    [MemberData(nameof(Changes))]
    public async Task AReadOnlyValueThatIsNotAsReadAtItsPlaceIsRefused(string place, string[] stored, string[] sent)
    {
        var id = await SystemCreateAsync(place, stored);
        var before = Stored(id);

        foreach (var write in new Func<Task>[]
        {
            () => _database.PatchAsync(Editor, id, new() { [Field(place)] = Value(place, sent) }, 1),
            () => _database.UpdateAsync(Editor, id, new() { ["title"] = "T", [Field(place)] = Value(place, sent) }, 1),
        })
        {
            var errors = (await Should.ThrowAsync<ValidationException>(write)).Errors;
            errors.ShouldNotBeEmpty();
            errors.ShouldAllBe(error => error.Path == StampPath(place) && error.Code == AuthorizationErrorCodes.FieldNotWritable);
        }

        Stored(id).ShouldBe(before);
    }

    // A read-only value is compared with the stored item the write pairs its item with, by visible content and then by
    // order, which is the one the composer keeps the stored value from.
    public static TheoryData<string, string[], string[]> Moves()
    {
        var data = new TheoryData<string, string[], string[]>();
        foreach (var place in AllPlaces.Where(place => place != "seo"))
        {
            data.Add(place, [Card("B", "TWO"), Card("A", "ONE")], ["TWO", "ONE"]);
            data.Add(place, [Card("B", "TWO")], ["TWO"]);
            data.Add(place, ["""{"label":"N"}""", Card("A", "ONE"), Card("B", "TWO")], ["ONE", "TWO"]);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Moves))]
    public async Task AReadOnlyValueSentWithItsItemMovesWithIt(string place, string[] sent, string[] stamps)
    {
        var id = await SystemCreateAsync(place, Card("A", "ONE"), Card("B", "TWO"));

        await _database.PatchAsync(Editor, id, new() { [Field(place)] = Value(place, sent) }, 1);

        var read = JsonSerializer.Serialize((await _database.GetAsync(Editor, id))![Field(place)]);
        Stamps.Where(read.Contains).OrderBy(stamp => read.IndexOf(stamp, StringComparison.Ordinal)).ShouldBe(stamps);
    }

    [Theory]
    [MemberData(nameof(Lists))]
    public async Task AReadOnlyValueLeftOutMovesWithItsBlock(string place)
    {
        var id = await SystemCreateAsync(place, Card("A", "ONE"), Card("B", "TWO"));

        await _database.PatchAsync(Editor, id, new() { [Field(place)] = Value(place, """{"label":"B"}""", """{"label":"A"}""") }, 1);

        var stored = Stored(id);
        stored.IndexOf("TWO", StringComparison.Ordinal).ShouldBeLessThan(stored.IndexOf("ONE", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Places))]
    public async Task AHiddenValueBesideAnUnchangedReadOnlyOneIsStillRefused(string place)
    {
        var id = await SystemCreateAsync(place, Card("A", "STAMPED"));

        var error = (await Should.ThrowAsync<ValidationException>(() =>
            _database.PatchAsync(Editor, id, new() { [Field(place)] = Value(place, """{"label":"A","stamp":"STAMPED","internal":"x"}""") }, 1))).Errors.ShouldHaveSingleItem();

        error.Path.ShouldBe(StampPath(place).Replace("stamp", "internal", StringComparison.Ordinal));
        error.Code.ShouldBe(AuthorizationErrorCodes.FieldNotWritable);
    }

    // What a new locale is sent that is the same as the locale it is read against is a fallback echo, so the values change.
    [Fact]
    public async Task ANewLocaleTakesAnUnchangedSharedValueButNotALocalizableOne()
    {
        var id = IdOf(await SystemAsync(service => service.CreateAsync(
            "article",
            new() { ["title"] = "T", ["cards"] = Value("cards", Card("ALPHA", "STAMPED")), ["body"] = Value("body", Card("ALPHA", "STAMPED")) },
            Token)));
        var read = (await _database.GetAsync(Editor, id))!;

        var error = (await Should.ThrowAsync<ValidationException>(() =>
            _database.UpdateAsync(Editor, id, new() { ["title"] = "Bonjour", ["body"] = Edited(read["body"], "ALPHA", "BRAVO") }, 0, locale: "fr"))).Errors.ShouldHaveSingleItem();
        error.Path.ShouldBe("body.stamp");

        await _database.UpdateAsync(Editor, id, new() { ["title"] = "Bonjour", ["cards"] = Edited(read["cards"], "ALPHA", "BRAVO") }, 0, locale: "fr");

        var french = JsonSerializer.Serialize(await _database.GetAsync(Editor, id, locale: "fr"));
        french.ShouldContain("BRAVO");
        french.ShouldContain("STAMPED");
    }
}
