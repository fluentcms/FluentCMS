using System.Text.Json;
using FluentCMS.Core.ChangeSets;

namespace FluentCMS.Tests.ChangeSets;

public sealed class ValueDiffTests
{
    [Fact]
    public void Added_changed_and_removed_values_are_listed_and_unchanged_ones_left_out()
    {
        var diffs = ValueDiff.Compare(
            Values(("title", "\"Desk lamp\""), ("price", "10"), ("color", "\"black\"")),
            Values(("title", "\"Desk lamp\""), ("price", "12"), ("stock", "4")));

        Assert.Equal(["price", "stock", "color"], diffs.Select(diff => diff.Name));
        Assert.Equal([ValueChange.Changed, ValueChange.Added, ValueChange.Removed], diffs.Select(diff => diff.Change));
        Assert.Equal(["10", null, "\"black\""], diffs.Select(diff => diff.Before?.GetRawText()));
        Assert.Equal(["12", "4", null], diffs.Select(diff => diff.After?.GetRawText()));
    }

    [Fact]
    public void Values_follow_the_later_order_and_removed_ones_follow_the_earlier_order()
    {
        var diffs = ValueDiff.Compare(
            Values(("a", "1"), ("gone1", "1"), ("b", "1"), ("gone2", "1")),
            Values(("new1", "1"), ("b", "2"), ("a", "2"), ("new2", "1")));

        Assert.Equal(["new1", "b", "a", "new2", "gone1", "gone2"], diffs.Select(diff => diff.Name));
    }

    [Theory]
    [InlineData("""{"min":0,"max":10}""", """{"max":10,"min":0}""")]
    [InlineData("""{"min":1}""", """{"min":1.0}""")]
    [InlineData("100", "1e2")]
    [InlineData("\"A\"", "\"\\u0041\"")]
    public void Values_equal_as_JSON_are_not_changes(string before, string after)
    {
        Assert.Empty(ValueDiff.Compare(Values(("settings", before)), Values(("settings", after))));
    }

    [Theory]
    [InlineData("[1,2]", "[2,1]")]
    [InlineData("1", "\"1\"")]
    [InlineData("null", "false")]
    [InlineData("""{"min":1}""", """{"min":1,"max":2}""")]
    public void Values_that_differ_as_JSON_are_changes(string before, string after)
    {
        var diff = Assert.Single(ValueDiff.Compare(Values(("value", before)), Values(("value", after))));

        Assert.Equal(ValueChange.Changed, diff.Change);
    }

    [Fact]
    public void Names_are_matched_exactly()
    {
        var diffs = ValueDiff.Compare(Values(("Price", "1")), Values(("price", "1")));

        Assert.Equal([("price", ValueChange.Added), ("Price", ValueChange.Removed)], diffs.Select(diff => (diff.Name, diff.Change)));
    }

    [Fact]
    public void Values_outlive_the_documents_they_were_read_from()
    {
        IReadOnlyList<ValueDiff> diffs;
        using (var before = JsonDocument.Parse("""{"price":10}"""))
        using (var after = JsonDocument.Parse("""{"price":12}"""))
        {
            diffs = ValueDiff.Compare(before.RootElement.EnumerateObject().Select(Pair), after.RootElement.EnumerateObject().Select(Pair));
        }

        var diff = Assert.Single(diffs);
        Assert.Equal(10, diff.Before!.Value.GetInt32());
        Assert.Equal(12, diff.After!.Value.GetInt32());
    }

    private static KeyValuePair<string, JsonElement> Pair(JsonProperty property)
    {
        return new(property.Name, property.Value);
    }

    private static List<KeyValuePair<string, JsonElement>> Values(params (string Name, string Json)[] values)
    {
        return values.Select(value => new KeyValuePair<string, JsonElement>(value.Name, Element(value.Json))).ToList();
    }

    private static JsonElement Element(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
