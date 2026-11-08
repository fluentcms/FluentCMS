using System.Text.Json.Nodes;
using FluentCMS.Core.Content;
using FluentCMS.Publishing;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Content;

public class VersionDiffTests
{
    private static JsonObject Hero(string id, string heading) => new() { ["type"] = "hero", ["id"] = id, ["heading"] = heading };

    private static JsonObject Quote(string id, string text) => new() { ["type"] = "quote", ["id"] = id, ["text"] = text };

    private static List<(string Path, VersionChangeKind Kind)> Diff(JsonArray before, JsonArray after) =>
        [.. VersionDiff.Between(new JsonObject { ["sections"] = before }, new JsonObject { ["sections"] = after }).Select(change => (change.Path, change.Kind))];

    [Fact]
    public void ABlockInsertedAtTheTopIsOneChangeAndTheBlocksAfterItAreNotChanged()
    {
        Diff(
            [Hero("a", "One"), Hero("b", "Two")],
            [Quote("c", "New"), Hero("a", "One"), Hero("b", "Two")]).ShouldBe([("sections[0]", VersionChangeKind.Added)]);
    }

    [Fact]
    public void AnEditIsReportedAtThePathOfTheBlockNow()
    {
        var change = VersionDiff.Between(
            new JsonObject { ["sections"] = new JsonArray(Hero("a", "One"), Hero("b", "Two")) },
            new JsonObject { ["sections"] = new JsonArray(Quote("c", "New"), Hero("a", "One"), Hero("b", "Changed")) })
            .Where(change => change.Kind == VersionChangeKind.Changed)
            .ShouldHaveSingleItem();

        change.Path.ShouldBe("sections[2].heading");
        change.Before!.GetValue<string>().ShouldBe("Two");
        change.After!.GetValue<string>().ShouldBe("Changed");
    }

    [Fact]
    public void ARemovedBlockIsReportedAtItsEarlierIndex()
    {
        Diff(
            [Hero("a", "One"), Hero("b", "Two"), Hero("c", "Three")],
            [Hero("a", "One"), Hero("c", "Three")]).ShouldBe([("sections[1]", VersionChangeKind.Removed)]);
    }

    [Fact]
    public void AMovedBlockIsRemovedWhereItWasAndAddedWhereItIsAndTheOthersStay()
    {
        Diff(
            [Hero("a", "One"), Hero("b", "Two"), Hero("c", "Three")],
            [Hero("b", "Two"), Hero("c", "Three"), Hero("a", "One")]).ShouldBe(
            [("sections[2]", VersionChangeKind.Added), ("sections[0]", VersionChangeKind.Removed)]);
    }

    [Fact]
    public void AReorderMovesTheFewestBlocks()
    {
        static JsonArray Of(string ids) => [.. ids.Select(id => Hero(id.ToString(), id.ToString()))];

        var swaps = Diff(Of("abcde"), Of("badce"));
        swaps.Count(change => change.Kind == VersionChangeKind.Added).ShouldBe(2);
        swaps.Count(change => change.Kind == VersionChangeKind.Removed).ShouldBe(2);

        var reversed = Diff(Of("abcd"), Of("dcba"));
        reversed.Count.ShouldBe(6);
        reversed.ShouldNotContain(change => change.Kind == VersionChangeKind.Changed);
    }

    [Fact]
    public void ABlockReplacedByAnotherIdIsARemovalAndAnAddition()
    {
        Diff([Hero("a", "One")], [Quote("b", "One")]).ShouldBe(
            [("sections[0]", VersionChangeKind.Added), ("sections[0]", VersionChangeKind.Removed)]);
    }

    [Fact]
    public void AListWithAnItemWithoutAnIdComparesByPosition()
    {
        var withoutId = new JsonObject { ["type"] = "hero", ["heading"] = "New" };

        Diff([Hero("a", "One"), Hero("b", "Two")], [withoutId, Hero("a", "One"), Hero("b", "Two")]).ShouldBe(
            [
                ("sections[0].id", VersionChangeKind.Removed),
                ("sections[0].heading", VersionChangeKind.Changed),
                ("sections[1].id", VersionChangeKind.Changed),
                ("sections[1].heading", VersionChangeKind.Changed),
                ("sections[2]", VersionChangeKind.Added),
            ]);
    }

    [Fact]
    public void AListThatRepeatsAnIdComparesByPosition()
    {
        Diff([Hero("a", "One"), Hero("b", "Two")], [Hero("a", "One"), Hero("a", "Two")]).ShouldBe([("sections[1].id", VersionChangeKind.Changed)]);
    }

    [Fact]
    public void ValuesAndNestedListsWithoutIdsStillCompareByPosition()
    {
        var changes = VersionDiff.Between(
            new JsonObject { ["labels"] = new JsonArray("net"), ["faq"] = new JsonArray(new JsonObject { ["q"] = "1" }, new JsonObject { ["q"] = "2" }) },
            new JsonObject { ["labels"] = new JsonArray("sql", "net"), ["faq"] = new JsonArray(new JsonObject { ["q"] = "0" }, new JsonObject { ["q"] = "1" }, new JsonObject { ["q"] = "2" }) });

        changes.Select(change => (change.Path, change.Kind)).ShouldBe(
            [
                ("labels[0]", VersionChangeKind.Changed),
                ("labels[1]", VersionChangeKind.Added),
                ("faq[0].q", VersionChangeKind.Changed),
                ("faq[1].q", VersionChangeKind.Changed),
                ("faq[2]", VersionChangeKind.Added),
            ]);
    }

    [Fact]
    public void BlocksNestedInABlockPairByIdToo()
    {
        var before = new JsonObject { ["type"] = "hero", ["id"] = "a", ["inner"] = new JsonArray(Quote("x", "1"), Quote("y", "2")) };
        var after = new JsonObject { ["type"] = "hero", ["id"] = "a", ["inner"] = new JsonArray(Quote("z", "0"), Quote("x", "1"), Quote("y", "2")) };

        Diff([before], [after]).ShouldBe([("sections[0].inner[0]", VersionChangeKind.Added)]);
    }
}
