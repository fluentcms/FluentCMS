#pragma warning disable CA1861
using FluentCMS.Content;
using FluentCMS.Core.Tests.Schema.Samples;
using FluentCMS.Media;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Content.Validation;

public class DeclarativeRuleTests
{
    private static readonly SchemaModel Rules = ValidationFixtures.Model(thing => thing
        .Field("name", "text", f => f.Required())
        .Field("code", "text", f => f.Length(3, 5).Pattern("^[a-z]+$"))
        .Field("count", "integer", f => f.Range(1, 10))
        .Field("opensAt", "dateTime", f => f.Range(1_577_836_800, 1_893_456_000))
        .Field("day", "date", f => f.Range(0, 100))
        .Field("kind", "select", f => f.Setting(FieldSettings.Options, new[] { "a", "b" }))
        .Field("labels", "select", f => f.Multiple().Setting(FieldSettings.Options, new[] { "a", "b" }))
        .Field("tags", "list", f => f.Multiple().Length(2, 3))
        .Field("scores", "list", f => f.Multiple().Range(0, 5)));

    private static async Task<string[]> Run(Dictionary<string, object?> values, ValidationMode mode = ValidationMode.Publish)
    {
        values.TryAdd("name", "n");
        return ValidationFixtures.Describe(await ValidationFixtures.ValidateAsync(Rules, "thing", values, mode));
    }

    [Fact]
    public async Task ADraftMissingATitleIsValidAndPublishingItFails()
    {
        var values = new Dictionary<string, object?> { ["category"] = ArticleCategory.News };

        (await ValidationFixtures.ValidateArticleAsync(values, ValidationMode.Draft)).IsValid.ShouldBeTrue();

        var result = await ValidationFixtures.ValidateArticleAsync(values, ValidationMode.Publish);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem().ShouldBe(new ValidationError("title", ValidationErrorCodes.Required, "A value is required."));
    }

    [Fact]
    public async Task AWhitespaceTitleCountsAsMissing()
    {
        var result = await ValidationFixtures.ValidateArticleAsync(new() { ["title"] = "  " }, ValidationMode.Publish);

        ValidationFixtures.Describe(result).ShouldBe(["title|FCMS2101"]);
    }

    [Fact]
    public async Task VideoUrlIsRequiredOnlyForAVideo()
    {
        var video = new Dictionary<string, object?> { ["title"] = "T", ["category"] = ArticleCategory.Video };
        var news = new Dictionary<string, object?> { ["title"] = "T", ["category"] = ArticleCategory.News };

        ValidationFixtures.Describe(await ValidationFixtures.ValidateArticleAsync(video, ValidationMode.Publish)).ShouldBe(["videoUrl|FCMS2110"]);
        (await ValidationFixtures.ValidateArticleAsync(video, ValidationMode.Draft)).IsValid.ShouldBeTrue();
        (await ValidationFixtures.ValidateArticleAsync(news, ValidationMode.Publish)).IsValid.ShouldBeTrue();

        video["videoUrl"] = "https://example.com/v";
        (await ValidationFixtures.ValidateArticleAsync(video, ValidationMode.Publish)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task ARequiredFieldThatIsAlsoRequiredWhenReportsOnce()
    {
        var model = ValidationFixtures.Model(thing => thing
            .Field("a", "text")
            .Field("b", "text", f => f.Required().RequiredWhen(Condition.Field("a").IsEmpty())));

        var result = await ValidationFixtures.ValidateAsync(model, "thing", new(), ValidationMode.Publish);

        ValidationFixtures.Describe(result).ShouldBe(["b|FCMS2101"]);
    }

    [Fact]
    public async Task AHiddenFieldWithABadValueIsNotReported()
    {
        var model = ValidationFixtures.Model(thing => thing
            .Field("category", "select", f => f.Setting(FieldSettings.Options, new[] { "news", "video" }))
            .Field("videoUrl", "text", f => f
                .Length(null, 5)
                .Required()
                .VisibleWhen(Condition.Field("category").Equals("video"))));

        var tooLong = new Dictionary<string, object?> { ["category"] = "news", ["videoUrl"] = "far too long" };
        var shown = new Dictionary<string, object?> { ["category"] = "video", ["videoUrl"] = "far too long" };

        (await ValidationFixtures.ValidateAsync(model, "thing", tooLong, ValidationMode.Publish)).IsValid.ShouldBeTrue();
        ValidationFixtures.Describe(await ValidationFixtures.ValidateAsync(model, "thing", shown, ValidationMode.Publish)).ShouldBe(["videoUrl|FCMS2103"]);

        var missing = new Dictionary<string, object?> { ["category"] = "news" };
        (await ValidationFixtures.ValidateAsync(model, "thing", missing, ValidationMode.Publish)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task ARequiredFieldIsCheckedOnPublishOnly()
    {
        var values = new Dictionary<string, object?> { ["name"] = "" };

        (await Run(values, ValidationMode.Draft)).ShouldBeEmpty();
        (await Run(values)).ShouldBe(["name|FCMS2101"]);
    }

    [Theory]
    [InlineData("abc", null)]
    [InlineData("abcde", null)]
    [InlineData("ab", "code|FCMS2102")]
    [InlineData("abcdef", "code|FCMS2103")]
    [InlineData("ABC", "code|FCMS2106")]
    [InlineData("a1", "code|FCMS2102,code|FCMS2106")]
    public async Task TextIsCheckedForLengthAndPattern(string code, string? expected)
    {
        (await Run(new() { ["code"] = code })).ShouldBe(expected?.Split(',') ?? []);
    }

    [Theory]
    [InlineData(1, null)]
    [InlineData(10, null)]
    [InlineData(0, "count|FCMS2104")]
    [InlineData(11, "count|FCMS2105")]
    public async Task NumbersAreCheckedAgainstTheRange(int count, string? expected)
    {
        (await Run(new() { ["count"] = count })).ShouldBe(expected is null ? [] : [expected]);
        (await Run(new() { ["count"] = (decimal)count })).ShouldBe(expected is null ? [] : [expected]);
    }

    [Fact]
    public async Task DatesAreNotRangeChecked()
    {
        (await Run(new() { ["opensAt"] = new DateTimeOffset(2019, 1, 1, 0, 0, 0, TimeSpan.Zero), ["day"] = new DateOnly(2024, 1, 1) })).ShouldBeEmpty();
        (await Run(new() { ["opensAt"] = new DateTime(2031, 1, 1, 0, 0, 0, DateTimeKind.Unspecified) })).ShouldBeEmpty();
    }

    [Fact]
    public async Task AFloatBeyondWhatADecimalHoldsIsOutOfRangeInItsDirection()
    {
        (await Run(new() { ["count"] = 1e300 })).ShouldBe(["count|FCMS2105"]);
        (await Run(new() { ["count"] = -1e300 })).ShouldBe(["count|FCMS2104"]);
        (await Run(new() { ["count"] = double.PositiveInfinity })).ShouldBe(["count|FCMS2105"]);
        (await Run(new() { ["count"] = float.NegativeInfinity })).ShouldBe(["count|FCMS2104"]);
        (await Run(new() { ["count"] = double.NaN })).ShouldBeEmpty();
        (await Run(new() { ["count"] = 5.5 })).ShouldBeEmpty();
    }

    [Fact]
    public async Task APatternMatchesAnywhereInTheValueUnlessAnchored()
    {
        var model = ValidationFixtures.Model(thing => thing
            .Field("loose", "text", f => f.Pattern("[0-9]+"))
            .Field("anchored", "text", f => f.Pattern("^[0-9]+$")));

        var result = await ValidationFixtures.ValidateAsync(model, "thing", new() { ["loose"] = "abc123def", ["anchored"] = "abc123def" }, ValidationMode.Publish);

        ValidationFixtures.Describe(result).ShouldBe(["anchored|FCMS2106"]);
    }

    [Fact]
    public async Task ADefaultMediaRefDoesNotSatisfyRequired()
    {
        var model = ValidationFixtures.Model(thing => thing.Field("hero", "media", f => f.Required()));

        ValidationFixtures.Describe(await ValidationFixtures.ValidateAsync(model, "thing", new() { ["hero"] = default(MediaRef) }, ValidationMode.Publish)).ShouldBe(["hero|FCMS2101"]);
        (await ValidationFixtures.ValidateAsync(model, "thing", new() { ["hero"] = new MediaRef(Guid.NewGuid()) }, ValidationMode.Publish)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task ASingleSelectMustBeAnOption()
    {
        (await Run(new() { ["kind"] = "a" })).ShouldBeEmpty();
        (await Run(new() { ["kind"] = "c" })).ShouldBe(["kind|FCMS2107"]);
    }

    [Fact]
    public async Task AMultiSelectChecksEachItemAtItsOwnPath()
    {
        (await Run(new() { ["labels"] = new[] { "a", "b" } })).ShouldBeEmpty();
        (await Run(new() { ["labels"] = new[] { "a", "x", "y" } })).ShouldBe(["labels[1]|FCMS2107", "labels[2]|FCMS2107"]);
    }

    [Fact]
    public async Task EnumMembersMatchTheirCamelCaseOptions()
    {
        var result = await ValidationFixtures.ValidateArticleAsync(new() { ["title"] = "T", ["category"] = ArticleCategory.Opinion }, ValidationMode.Publish);

        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("slug", 200)]
    [InlineData("link", 2048)]
    [InlineData("contact", 254)]
    public async Task AFieldWithoutAMaximumIsLimitedToTheLengthOfItsColumn(string field, int length)
    {
        var model = ValidationFixtures.Model(thing => thing
            .Field("slug", "slug")
            .Field("link", "url")
            .Field("contact", "email")
            .Field("notes", "longText"));

        string Value(int size) => new('a', size);

        (await ValidationFixtures.ValidateAsync(model, "thing", new() { [field] = Value(length), ["notes"] = Value(100_000) }, ValidationMode.Draft)).IsValid.ShouldBeTrue();

        var result = await ValidationFixtures.ValidateAsync(model, "thing", new() { [field] = Value(length + 1) }, ValidationMode.Draft);
        result.Errors.ShouldHaveSingleItem().ShouldBe(new ValidationError(field, ValidationErrorCodes.MaxLength, $"Must have at most {length} characters, has {length + 1}."));
    }

    [Fact]
    public async Task ASelectIsLimitedTo64OrItsMaximumLength()
    {
        var longest = new string('x', 80);
        var model = ValidationFixtures.Model(thing => thing
            .Field("short", "select", f => f.Setting(FieldSettings.Options, new[] { "a" }))
            .Field("long", "select", f => f.Length(null, 80).Setting(FieldSettings.Options, new[] { "a", longest })));

        (await ValidationFixtures.ValidateAsync(model, "thing", new() { ["long"] = longest }, ValidationMode.Publish)).IsValid.ShouldBeTrue();
        ValidationFixtures.Describe(await ValidationFixtures.ValidateAsync(model, "thing", new() { ["short"] = new string('a', 65) }, ValidationMode.Publish))
            .ShouldBe(["short|FCMS2103", "short|FCMS2107"]);
    }

    [Fact]
    public async Task ADeclaredMaximumReplacesTheDefault()
    {
        var model = ValidationFixtures.Model(thing => thing
            .Field("longer", "slug", f => f.Length(null, 300))
            .Field("shorter", "url", f => f.Length(null, 30)));

        (await ValidationFixtures.ValidateAsync(model, "thing", new() { ["longer"] = new string('a', 300) }, ValidationMode.Publish)).IsValid.ShouldBeTrue();
        ValidationFixtures.Describe(await ValidationFixtures.ValidateAsync(model, "thing", new() { ["shorter"] = "https://example.com/" + new string('a', 11) }, ValidationMode.Publish))
            .ShouldBe(["shorter|FCMS2103"]);
    }

    [Fact]
    public async Task TheDefaultHoldsInsideABlock()
    {
        var model = ValidationFixtures.Model(
            thing => thing.Field("body", "blocks", f => f.Setting(FieldSettings.Blocks, new[] { "card" })),
            others => others.AddBlock("card", block => block.Field("link", "url")));

        var body = new[] { new Dictionary<string, object?> { ["type"] = "card", ["link"] = new string('a', 2049) } };

        ValidationFixtures.Describe(await ValidationFixtures.ValidateAsync(model, "thing", new() { ["body"] = body }, ValidationMode.Draft)).ShouldBe(["body[0].link|FCMS2103"]);
    }

    [Fact]
    public async Task LengthOnAMultipleFieldCountsItems()
    {
        (await Run(new() { ["tags"] = new[] { "a", "b" } })).ShouldBeEmpty();

        var few = await ValidationFixtures.ValidateAsync(Rules, "thing", new() { ["name"] = "n", ["tags"] = new[] { "a" } }, ValidationMode.Publish);
        few.Errors.ShouldHaveSingleItem().ShouldBe(new ValidationError("tags", ValidationErrorCodes.MinLength, "Must have at least 2 items, has 1."));

        (await Run(new() { ["tags"] = new[] { "a", "b", "c", "d" } })).ShouldBe(["tags|FCMS2103"]);
    }

    [Fact]
    public async Task RangeOnAMultipleFieldChecksEachItem()
    {
        (await Run(new() { ["scores"] = new[] { 1, 6, -1 } })).ShouldBe(["scores[1]|FCMS2105", "scores[2]|FCMS2104"]);
    }

    [Fact]
    public async Task AnEmptyValueSkipsTheFormatRules()
    {
        (await Run(new() { ["code"] = null, ["count"] = null, ["kind"] = "", ["labels"] = Array.Empty<string>() })).ShouldBeEmpty();
    }

    [Fact]
    public async Task ErrorsFollowTheDeclarationOrderOfTheFields()
    {
        var values = new Dictionary<string, object?>
        {
            ["scores"] = new[] { 9 },
            ["kind"] = "z",
            ["count"] = 99,
            ["code"] = "A",
            ["name"] = "",
        };

        var expected = new[] { "name|FCMS2101", "code|FCMS2102", "code|FCMS2106", "count|FCMS2105", "kind|FCMS2107", "scores[0]|FCMS2105" };

        for (var i = 0; i < 3; i++)
        {
            (await Run(values)).ShouldBe(expected);
        }
    }
}
