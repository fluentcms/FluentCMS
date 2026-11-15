using FluentCMS.Content;
using FluentCMS.Core.Identity.PreviewTokens;
using FluentCMS.Core.Schema;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Identity;

// The URL templates of the settings area "preview" (#3099): what a template may be, and how a token's answer fills it in.
public sealed class PreviewUrlTemplateTests
{
    private const string Token = "eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiJ4In0.c2lnbmF0dXJl";

    private static readonly Guid Id = new("0193a000-0000-7000-8000-000000000001");

    private static readonly SchemaModel Schema = new SchemaBuilder()
        .AddCollection("story", type => type.Localized()
            .Field("title", "text", field => field.Localizable())
            .Field("slug", "slug")
            .Field("rank", "integer")
            .Field("featured", "boolean")
            .Field("day", "date")
            .Field("internal", "text", field => field.Hidden())
            .Field("body", "richText")
            .Field("related", "relation", field => field.Relation("story")))
        .AddCollection("note", type => type.Field("title", "text"))
        .Build();

    private static SchemaType Story => Schema.FindContentType("story")!;

    [Fact]
    public void AValueIsEscapedAndTheTokenGoesWhereItsPlaceholderIs()
    {
        var template = Parse("https://www.example.com/{locale}/stories/{slug}?title={title}&preview={token}#top");

        var url = template.Fill(Entry(("slug", "fish"), ("title", "Fish & chips/2?#x=1 %41")), Id, "fr-CA", Token);

        url.ShouldBe($"https://www.example.com/fr-CA/stories/fish?title=Fish%20%26%20chips%2F2%3F%23x%3D1%20%2541&preview={Token}#top");
        var parsed = new Uri(url!);
        (parsed.Host, parsed.AbsolutePath, parsed.Fragment).ShouldBe(("www.example.com", "/fr-CA/stories/fish", "#top"));
    }

    [Theory]
    [InlineData("//evil.example/x")]
    [InlineData("@evil.example")]
    [InlineData("../../admin")]
    [InlineData("x\\..\\..")]
    [InlineData("javascript:alert(1)")]
    public void NoValueChangesTheOriginOrAddsASegment(string slug)
    {
        var url = Parse("https://www.example.com/stories/{slug}?preview={token}").Fill(Entry(("slug", slug)), Id, null, Token);

        var parsed = new Uri(url!);
        (parsed.Scheme, parsed.Authority).ShouldBe(("https", "www.example.com"));
        parsed.Segments.Length.ShouldBe(3);
        parsed.Query.ShouldBe($"?preview={Token}");
    }

    [Theory]
    [InlineData("https://www.example.com/stories/{slug}?preview={token}", "https://www.example.com/stories/fish")]
    [InlineData("https://www.example.com/stories/{slug}?preview={token}&id={id}", "https://www.example.com/stories/fish?id=0193a000-0000-7000-8000-000000000001")]
    [InlineData("https://www.example.com/stories?id={id}&preview={token}#{slug}", "https://www.example.com/stories?id=0193a000-0000-7000-8000-000000000001#fish")]
    [InlineData("https://www.example.com?preview={token}#top", "https://www.example.com#top")]
    public void ThePublishedUrlLeavesOutTheParameterOfTheToken(string text, string published)
    {
        var template = Parse(text);

        template.Fill(Entry(("slug", "fish")), Id, null, token: null).ShouldBe(published);
        template.Fill(Entry(("slug", "fish")), Id, null, Token)!.ShouldContain(Token);
    }

    [Fact]
    public void AnEscapeOfTheTemplateStaysAsItIs()
    {
        var template = Parse("https://www.example.com/a%20b/{slug}?q=%2Fx&preview={token}");

        template.Fill(Entry(("slug", "fish")), Id, null, Token).ShouldBe($"https://www.example.com/a%20b/fish?q=%2Fx&preview={Token}");
    }

    [Fact]
    public void AScalarFieldIsWrittenAsItsJsonIs()
    {
        var template = Parse("https://www.example.com/{day}/{rank}?featured={featured}&preview={token}");

        template.Fill(Entry(("day", new DateOnly(2027, 3, 1)), ("rank", 7L), ("featured", true)), Id, null, Token)
            .ShouldBe($"https://www.example.com/2027-03-01/7?featured=true&preview={Token}");
    }

    // A field the caller's read left out, one that is empty, one that is no longer one value, and dots are no URL at all.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("left out")]
    [InlineData("not a scalar")]
    public void APlaceholderWithoutAValueGivesNoUrl(string? slug)
    {
        var entry = slug switch
        {
            "left out" => Entry(),
            "not a scalar" => Entry(("slug", new List<string> { "a", "b" })),
            _ => Entry(("slug", slug)),
        };

        Parse("https://www.example.com/stories/{slug}?preview={token}").Fill(entry, Id, null, Token).ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("www.example.com/{slug}?preview={token}")]
    [InlineData("/stories/{slug}?preview={token}")]
    [InlineData("ftp://www.example.com/{slug}?preview={token}")]
    [InlineData("javascript:alert(1)//://www.example.com?preview={token}")]
    [InlineData("https:///stories?preview={token}")]
    [InlineData("https://{slug}.example.com/?preview={token}")]
    [InlineData("https://www.example.com:{rank}/?preview={token}")]
    [InlineData("https://user@www.example.com/?preview={token}")]
    [InlineData("https://www.example.com\\@evil.example/?preview={token}")]
    [InlineData("https://www.example.com/a b?preview={token}")]
    [InlineData("https://www.example.com/%{slug}?preview={token}")]
    [InlineData("https://www.example.com/.%{slug}?preview={token}")]
    [InlineData("https://www.example.com/%2{slug}?preview={token}")]
    [InlineData("https://www.example.com/%zz/{slug}?preview={token}")]
    [InlineData("https://www.example.com/{slug}?preview={token}&x=%")]
    [InlineData("https://www.example.com//{slug}?preview={token}")]
    [InlineData("https://www.example.com//?preview={token}")]
    [InlineData("https://www.example.com/{slug?preview={token}")]
    [InlineData("https://www.example.com/{Slug}?preview={token}")]
    [InlineData("https://www.example.com/}?preview={token}")]
    [InlineData("https://www.example.com/{slug}")]
    [InlineData("https://www.example.com/{token}")]
    [InlineData("https://www.example.com/?preview=x{token}")]
    [InlineData("https://www.example.com/?{token}")]
    [InlineData("https://www.example.com/?={token}")]
    [InlineData("https://www.example.com/?preview={token}&again={token}")]
    [InlineData("https://www.example.com/#preview={token}")]
    public void ATemplateThatIsNotAnHttpUrlWithTheTokenInItsQueryIsRefused(string text)
    {
        List<ValidationError> errors = [];

        PreviewUrlTemplate.Parse(text, Story, "templates.story", errors).ShouldBeNull();

        var error = errors.ShouldHaveSingleItem();
        (error.Path, error.Code).ShouldBe(("templates.story", PreviewTokenErrorCodes.InvalidUrlTemplate));
    }

    [Fact]
    public void ATemplateLongerThanTheMostIsRefused()
    {
        List<ValidationError> errors = [];

        PreviewUrlTemplate.Parse($"https://www.example.com/{new string('a', PreviewUrlTemplate.MaxLength)}?preview={{token}}", Story, "", errors).ShouldBeNull();

        errors.ShouldHaveSingleItem().Code.ShouldBe(PreviewTokenErrorCodes.InvalidUrlTemplate);
    }

    [Theory]
    [InlineData("story", "{missing}")]
    [InlineData("story", "{internal}")]
    [InlineData("story", "{body}")]
    [InlineData("story", "{related}")]
    [InlineData("note", "{locale}")]
    [InlineData("note", "{slug}")]
    public void APlaceholderThatNamesNoValueOfTheTypeIsRefused(string type, string placeholder)
    {
        List<ValidationError> errors = [];

        PreviewUrlTemplate.Parse($"https://www.example.com/{placeholder}?preview={{token}}", Schema.FindContentType(type)!, "templates." + type, errors).ShouldBeNull();

        var error = errors.ShouldHaveSingleItem();
        (error.Path, error.Code).ShouldBe(("templates." + type, PreviewTokenErrorCodes.UnknownTemplateName));
    }

    [Fact]
    public void TheCheckOfTheAreaRefusesATemplateOfATypeTheSchemaDoesNotHave()
    {
        var check = new PreviewSettingsCheck(Schema);
        var settings = new PreviewSettings
        {
            Templates =
            {
                ["story"] = "https://www.example.com/{locale}/{slug}?preview={token}",
                ["nope"] = "https://www.example.com/?preview={token}",
                ["note"] = "https://www.example.com/{locale}?preview={token}",
            },
        };

        check.Check(settings).Select(error => (error.Path, error.Code)).ShouldBe(
        [
            ("templates.nope", PreviewTokenErrorCodes.UnknownTemplateName),
            ("templates.note", PreviewTokenErrorCodes.UnknownTemplateName),
        ]);
        check.Check(new PreviewSettings()).ShouldBeEmpty();
    }

    private static PreviewUrlTemplate Parse(string text)
    {
        List<ValidationError> errors = [];
        var template = PreviewUrlTemplate.Parse(text, Story, "templates.story", errors);
        errors.ShouldBeEmpty();
        return template.ShouldNotBeNull();
    }

    private static ContentDocument Entry(params (string Name, object? Value)[] values) =>
        new(values.Select(value => KeyValuePair.Create(value.Name, value.Value)));
}
