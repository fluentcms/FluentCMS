using FluentCMS.Content;
using System.Text.Json.Nodes;
using FluentCMS.Core.Content.Hooks;
using FluentCMS.Core.Tests.Schema.Samples;
using FluentCMS.Media;
using FluentCMS.Schema;
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

    [Fact]
    public void EqualButDistinctJsonObjectsHaveNoChanges()
    {
        var previous = Map(("seo", JsonNode.Parse("""{"metaTitle":"m","tags":["a","b"],"n":{"x":1}}""")), ("flag", JsonValue.Create(true)));
        var current = Map(("seo", JsonNode.Parse("""{"metaTitle":"m","tags":["a","b"],"n":{"x":1}}""")), ("flag", JsonValue.Create(true)));

        ContentChanges.Between(previous, current).ShouldBeEmpty();
    }

    [Fact]
    public void AChangeInsideJsonIsReportedAtItsPath()
    {
        var previous = Map(("seo", JsonNode.Parse("""{"metaTitle":"m","tags":["a","b"],"n":{"x":1}}""")), ("count", JsonValue.Create(1)));
        var current = Map(("seo", JsonNode.Parse("""{"metaTitle":"n","tags":["a","c"],"n":{"x":2}}""")), ("count", JsonValue.Create(2)));

        ContentChanges.Between(previous, current).ShouldBe(["seo.metaTitle", "seo.tags[1]", "seo.n.x", "count"]);
    }

    [Fact]
    public void AJsonArrayWhoseLengthChangedIsReportedAsTheArray()
    {
        var previous = Map(("sections", JsonNode.Parse("""[{"type":"hero"}]""")));
        var current = Map(("sections", JsonNode.Parse("""[{"type":"hero"},{"type":"quote"}]""")));

        ContentChanges.Between(previous, current).ShouldBe(["sections"]);
    }

    [Fact]
    public void JsonAndDictionariesCompareAsTheSameValue()
    {
        var previous = Map(("seo", JsonNode.Parse("""{"metaTitle":"m","count":3}""")));
        var current = Map(("seo", Map(("metaTitle", "m"), ("count", 3))));

        ContentChanges.Between(previous, current).ShouldBeEmpty();
    }

    [Fact]
    public void JsonNullAndAMissingValueAreTheSame()
    {
        var previous = Map(("seo", JsonNode.Parse("""{"metaTitle":null}""")));
        var current = Map(("seo", JsonNode.Parse("""{}""")));

        ContentChanges.Between(previous, current).ShouldBeEmpty();
    }

    [Fact]
    public void EqualButDistinctSdkValuesHaveNoChanges()
    {
        var id = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var previous = Map(
            ("body", new RichText("""{"type":"doc"}""")),
            ("author", new Ref<Author>(id)),
            ("tags", new RefList<Tag>([new Ref<Tag>(id), new Ref<Tag>(other)])),
            ("hero", new MediaRef(id)),
            ("images", new MediaRefList([new MediaRef(id)])),
            ("sections", new BlockList([new HeroBlock { Heading = "h", Image = new MediaRef(id) }, new QuoteBlock { Text = "t" }])));
        var current = Map(
            ("body", new RichText("""{"type":"doc"}""")),
            ("author", new Ref<Author>(id)),
            ("tags", new RefList<Tag>([new Ref<Tag>(id), new Ref<Tag>(other)])),
            ("hero", new MediaRef(id)),
            ("images", new MediaRefList([new MediaRef(id)])),
            ("sections", new BlockList([new HeroBlock { Heading = "h", Image = new MediaRef(id) }, new QuoteBlock { Text = "t" }])));

        ContentChanges.Between(previous, current).ShouldBeEmpty();
    }

    [Fact]
    public void ChangedSdkValuesAreReported()
    {
        var id = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var previous = Map(
            ("body", new RichText("""{"type":"doc"}""")),
            ("author", new Ref<Author>(id)),
            ("tags", new RefList<Tag>([new Ref<Tag>(id), new Ref<Tag>(other)])),
            ("hero", new MediaRef(id)),
            ("images", new MediaRefList([new MediaRef(id)])),
            ("sections", new BlockList([new HeroBlock { Heading = "h" }, new QuoteBlock { Text = "t" }])));
        var current = Map(
            ("body", new RichText("""{"type":"doc","content":[]}""")),
            ("author", new Ref<Author>(other)),
            ("tags", new RefList<Tag>([new Ref<Tag>(id), new Ref<Tag>(id)])),
            ("hero", new MediaRef(other)),
            ("images", new MediaRefList([new MediaRef(id), new MediaRef(other)])),
            ("sections", new BlockList([new HeroBlock { Heading = "changed" }, new GalleryBlock()])));

        ContentChanges.Between(previous, current).ShouldBe(["body", "author", "tags[1]", "hero", "images", "sections[0].heading", "sections[1]"]);
    }
}
