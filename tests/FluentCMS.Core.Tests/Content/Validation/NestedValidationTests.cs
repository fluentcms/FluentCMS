using FluentCMS.Content;
using FluentCMS.Core.Tests.Schema.Samples;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Content.Validation;

public class NestedValidationTests
{
    private static readonly SchemaModel Nested = ValidationFixtures.Model(
        thing => thing
            .Field("home", "component", f => f.Setting(FieldSettings.Component, "address"))
            .Field("addresses", "component", f => f.Multiple().Setting(FieldSettings.Component, "address"))
            .Field("body", "blocks", f => f.Setting(FieldSettings.Blocks, new[] { "paragraph" })),
        builder => builder
            .AddComponent("address", address => address
                .Field("street", "text", f => f.Required())
                .Field("city", "text", f => f.Length(null, 4)))
            .AddBlock("paragraph", block => block.Field("text", "text", f => f.Required()))
            .AddBlock("quote", block => block.Field("text", "text")));

    private sealed class Address
    {
        public string Street { get; set; } = "";

        public string? City { get; set; }
    }

    private static async Task<string[]> Run(SchemaModel model, Dictionary<string, object?> values, ValidationMode mode = ValidationMode.Publish) =>
        ValidationFixtures.Describe(await ValidationFixtures.ValidateAsync(model, "thing", values, mode));

    [Fact]
    public async Task ASingleComponentReportsUnderItsName()
    {
        var seo = new Seo { MetaTitle = new string('x', 71) };

        var result = await ValidationFixtures.ValidateArticleAsync(new() { ["title"] = "T", ["seo"] = seo }, ValidationMode.Publish);

        ValidationFixtures.Describe(result).ShouldBe(["seo.metaTitle|FCMS2103"]);
    }

    [Fact]
    public async Task AComponentCanBeADictionaryOrAnObject()
    {
        var expected = new[] { "home.street|FCMS2101", "home.city|FCMS2103" };

        (await Run(Nested, new() { ["home"] = new Dictionary<string, object?> { ["street"] = "", ["city"] = "Springfield" } })).ShouldBe(expected);
        (await Run(Nested, new() { ["home"] = new Address { Street = "", City = "Springfield" } })).ShouldBe(expected);
        (await Run(Nested, new() { ["home"] = new Address { Street = "Main", City = "Rome" } })).ShouldBeEmpty();
    }

    [Fact]
    public async Task ADraftSkipsRequiredInsideAComponent()
    {
        (await Run(Nested, new() { ["home"] = new Address { Street = "" } }, ValidationMode.Draft)).ShouldBeEmpty();
    }

    [Fact]
    public async Task ARepeatableComponentReportsUnderItsIndex()
    {
        var values = new Dictionary<string, object?>
        {
            ["addresses"] = new List<Address> { new() { Street = "Main" }, new() { Street = "", City = "Springfield" } },
        };

        (await Run(Nested, values)).ShouldBe(["addresses[1].street|FCMS2101", "addresses[1].city|FCMS2103"]);
    }

    [Fact]
    public async Task ABlockIsReadByItsTypeKeyOrItsClass()
    {
        var blocks = new object[]
        {
            new Dictionary<string, object?> { ["type"] = "paragraph", ["text"] = "ok" },
            new Dictionary<string, object?> { ["type"] = "paragraph", ["text"] = "" },
        };

        (await Run(Nested, new() { ["body"] = blocks })).ShouldBe(["body[1].text|FCMS2101"]);
    }

    [Fact]
    public async Task ABlockOfATypeTheFieldDoesNotAllowIsRejected()
    {
        var blocks = new object[]
        {
            new Dictionary<string, object?> { ["type"] = "quote", ["text"] = "x" },
            new Dictionary<string, object?> { ["type"] = "nothing" },
            new Dictionary<string, object?> { ["text"] = "no type" },
        };

        (await Run(Nested, new() { ["body"] = blocks })).ShouldBe(["body[0]|FCMS2107", "body[1]|FCMS2107", "body[2]|FCMS2107"]);
    }

    [Fact]
    public async Task ClassBlocksAreMatchedByTheirClassAndValidated()
    {
        var sections = new BlockList([new HeroBlock { Heading = "Hi" }, new QuoteBlock { Text = "" }, new QuoteBlock { Text = "x" }]);

        var result = await ValidationFixtures.ValidateArticleAsync(new() { ["title"] = "T", ["sections"] = sections }, ValidationMode.Publish);

        ValidationFixtures.Describe(result).ShouldBe(["sections[1].text|FCMS2101"]);
    }

    [Fact]
    public async Task ABlockClassThatIsNotInTheSchemaIsRejected()
    {
        var result = await ValidationFixtures.ValidateArticleAsync(new() { ["title"] = "T", ["sections"] = new BlockList([new Address()]) }, ValidationMode.Publish);

        ValidationFixtures.Describe(result).ShouldBe(["sections[0]|FCMS2107"]);
    }

    [Fact]
    public async Task AnAbsentComponentIsNotDescendedInto()
    {
        (await Run(Nested, new() { ["home"] = null })).ShouldBeEmpty();
    }
}
