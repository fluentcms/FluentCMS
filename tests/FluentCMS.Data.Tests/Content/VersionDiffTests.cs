using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

public sealed class VersionDiffTests : IDisposable
{
    private static readonly string[] OneLabel = ["net"];
    private static readonly string[] TwoLabels = ["net", "sql"];

    private readonly ServiceDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private static RichText Paragraphs(params string[] texts) => new(
        "{\"type\":\"doc\",\"content\":[" + string.Join(',', texts.Select(text => "{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"" + text + "\"}]}")) + "]}");

    private static ContentDocument Hero(string heading) => new() { ["type"] = "hero", ["heading"] = heading };

    private static ContentDocument Quote(string text) => new() { ["type"] = "quote", ["text"] = text };

    private async Task<IReadOnlyList<VersionChange>> DiffOfAsync(ContentDocument first, ContentDocument second)
    {
        first["title"] = Guid.NewGuid().ToString("N");
        var id = IdOf(await _database.CreateAsync(Editor, first));
        await _database.PatchAsync(Editor, id, second, 1);
        return await _database.DiffAsync(Editor, id, 1, 2);
    }

    [Fact]
    public async Task AChangeInsideABlockIsReportedAtItsPath()
    {
        var change = (await DiffOfAsync(
            new() { ["sections"] = new object[] { Hero("One"), Hero("Two") } },
            new() { ["sections"] = new object[] { Hero("One"), Hero("Changed") } })).ShouldHaveSingleItem();

        change.Path.ShouldBe("sections[1].heading");
        change.Kind.ShouldBe(VersionChangeKind.Changed);
        change.Before!.GetValue<string>().ShouldBe("Two");
        change.After!.GetValue<string>().ShouldBe("Changed");
    }

    [Fact]
    public async Task ABlockAddedRemovedOrOfAnotherTypeIsOneChange()
    {
        var added = (await DiffOfAsync(
            new() { ["sections"] = new object[] { Hero("One") } },
            new() { ["sections"] = new object[] { Hero("One"), Quote("New") } })).ShouldHaveSingleItem();
        added.Path.ShouldBe("sections[1]");
        added.Kind.ShouldBe(VersionChangeKind.Added);
        added.Before.ShouldBeNull();

        var removed = (await DiffOfAsync(
            new() { ["sections"] = new object[] { Hero("One"), Quote("Old") } },
            new() { ["sections"] = new object[] { Hero("One") } })).ShouldHaveSingleItem();
        removed.Path.ShouldBe("sections[1]");
        removed.Kind.ShouldBe(VersionChangeKind.Removed);
        removed.After.ShouldBeNull();

        // Another block takes the place of the hero, with an id of its own.
        var other = await DiffOfAsync(
            new() { ["sections"] = new object[] { Hero("One") } },
            new() { ["sections"] = new object[] { Quote("One") } });
        other.Select(change => (change.Path, change.Kind)).ShouldBe([("sections[0]", VersionChangeKind.Added), ("sections[0]", VersionChangeKind.Removed)]);
        other[0].After!["type"]!.GetValue<string>().ShouldBe("quote");
        other[1].Before!["type"]!.GetValue<string>().ShouldBe("hero");
    }

    [Fact]
    public async Task BlocksPairByTheirIdsSoABlockInsertedAtTheTopIsOneChange()
    {
        var id = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "Ids", ["sections"] = new object[] { Hero("One"), Hero("Two") } }));
        var sections = JsonNode.Parse(JsonSerializer.Serialize((await _database.GetAsync(Editor, id))!["sections"]))!.AsArray();
        sections[1]!["heading"] = "Changed";
        sections.Insert(0, new JsonObject { ["type"] = "quote", ["text"] = "New" });
        await _database.PatchAsync(Editor, id, new() { ["sections"] = sections }, 1);

        var changes = await _database.DiffAsync(Editor, id, 1, 2);

        changes.Select(change => (change.Path, change.Kind)).ShouldBe([("sections[0]", VersionChangeKind.Added), ("sections[2].heading", VersionChangeKind.Changed)]);
    }

    [Fact]
    public async Task RichTextComparesAsATree()
    {
        var changes = await DiffOfAsync(
            new() { ["body"] = Paragraphs("One", "Two") },
            new() { ["body"] = Paragraphs("One", "Changed", "Three") });

        changes.Select(change => (change.Path, change.Kind)).ShouldBe(
            [("body.content[1].content[0].text", VersionChangeKind.Changed), ("body.content[2]", VersionChangeKind.Added)]);
    }

    [Fact]
    public async Task ScalarsGroupsAndListsReportTheirOwnPaths()
    {
        var changes = await DiffOfAsync(
            new() { ["views"] = 1, ["seo"] = new ContentDocument { ["metaTitle"] = "One" }, ["labels"] = OneLabel },
            new() { ["views"] = 2, ["seo"] = new ContentDocument { ["metaTitle"] = "Two" }, ["labels"] = TwoLabels, ["region"] = "eu" });

        changes.Select(change => (change.Path, change.Kind)).ShouldBe(
            [
                ("views", VersionChangeKind.Changed),
                ("region", VersionChangeKind.Added),
                ("labels[1]", VersionChangeKind.Added),
                ("seo.metaTitle", VersionChangeKind.Changed),
            ],
            ignoreOrder: true);
    }

    [Fact]
    public async Task AVersionComparedWithItselfHasNoChanges()
    {
        var id = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "One", ["body"] = Paragraphs("x") }));

        (await _database.DiffAsync(Editor, id, 1, 1)).ShouldBeEmpty();
    }
}
