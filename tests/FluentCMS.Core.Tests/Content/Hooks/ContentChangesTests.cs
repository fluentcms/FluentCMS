using FluentCMS.Content;
using FluentCMS.Core.Content.Hooks;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Content.Hooks;

public class ContentChangesTests
{
    private static Dictionary<string, object?> Map(params (string Key, object? Value)[] values) =>
        values.ToDictionary(item => item.Key, item => item.Value);

    [Fact]
    public void ChangedScalarsAreListedInTheOrderOfTheEntry()
    {
        var previous = Map(("title", "a"), ("slug", "a"), ("count", 1L));
        var current = Map(("title", "b"), ("slug", "a"), ("count", 2L));

        ContentChanges.Between(previous, current).ShouldBe(["title", "count"]);
    }

    [Fact]
    public void EqualEntriesHaveNoChanges()
    {
        var previous = Map(("title", "a"), ("seo", Map(("metaTitle", "m"))), ("tags", new object[] { "x", "y" }));
        var current = Map(("title", "a"), ("seo", Map(("metaTitle", "m"))), ("tags", new object[] { "x", "y" }));

        ContentChanges.Between(previous, current).ShouldBeEmpty();
    }

    [Fact]
    public void NestedPathsUseTheDottedFormOfValidation()
    {
        var previous = Map(("seo", Map(("metaTitle", "m"), ("noIndex", false))));
        var current = Map(("seo", Map(("metaTitle", "n"), ("noIndex", false))));

        ContentChanges.Between(previous, current).ShouldBe(["seo.metaTitle"]);
    }

    [Fact]
    public void ListsOfTheSameLengthAreComparedByIndex()
    {
        var previous = Map(
            ("tags", new object?[] { "a", "b" }),
            ("sections", new object?[] { Map(("heading", "h1")), Map(("heading", "h2")) }));
        var current = Map(
            ("tags", new object?[] { "a", "c" }),
            ("sections", new object?[] { Map(("heading", "h1")), Map(("heading", "changed")) }));

        ContentChanges.Between(previous, current).ShouldBe(["tags[1]", "sections[1].heading"]);
    }

    [Fact]
    public void AListWhoseLengthChangedIsReportedAsTheList()
    {
        var previous = Map(("tags", new object?[] { "a" }));
        var current = Map(("tags", new object?[] { "a", "b" }));

        ContentChanges.Between(previous, current).ShouldBe(["tags"]);
    }

    [Fact]
    public void AMissingFieldCountsAsNullAndARemovedValueIsAChange()
    {
        var previous = Map(("title", "a"), ("note", null), ("seo", Map(("metaTitle", "m"))));
        var current = Map(("other", null));

        ContentChanges.Between(previous, current).ShouldBe(["title", "seo"]);
    }

    [Fact]
    public void CreatingListsTheFieldsThatHoldAValue()
    {
        var current = new ContentDocument { ["title"] = "a", ["note"] = null, ["seo"] = Map(("metaTitle", "m")) };

        ContentChanges.Between(null, current).ShouldBe(["title", "seo"]);
    }

    [Fact]
    public void NumbersCompareByValueWhateverTheirClrType()
    {
        var previous = Map(("count", 3), ("price", 1.5m));
        var current = Map(("count", 3L), ("price", 1.5m));

        ContentChanges.Between(previous, current).ShouldBeEmpty();
    }

    [Fact]
    public void ADocumentIsComparedLikeAnyDictionary()
    {
        var previous = new ContentDocument { ["seo"] = new ContentDocument { ["metaTitle"] = "m" } };
        var current = new ContentDocument { ["seo"] = new ContentDocument { ["metaTitle"] = "n" } };

        ContentChanges.Between(previous, current).ShouldBe(["seo.metaTitle"]);
    }
}
