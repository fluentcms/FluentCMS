using System.Text.Json.Nodes;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.ProseMirror;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.ProseMirror;

public sealed class RichTextLinksTests
{
    private static readonly SchemaModel Schema = SampleSchema.NewBuilder()
        .AddBlock("card", block => block.Field("note", "richText"))
        .AddCollection("article", type => type.Field("body", "richText"))
        .Build();

    private static RichText Paragraph(string inline) => new("""{"type":"doc","content":[{"type":"paragraph","content":[""" + inline + "]}]}");

    [Theory]
    [InlineData("javascript")]
    [InlineData("JavaScript")]
    [InlineData("data")]
    [InlineData("vbscript")]
    [InlineData("blob")]
    [InlineData("file")]
    [InlineData("about")]
    [InlineData("sms:")]
    [InlineData("")]
    [InlineData("1ftp")]
    public void ASchemeThatIsForbiddenOrNoSchemeStopsTheHost(string scheme)
    {
        var options = new RichTextOptions();
        options.LinkSchemes.Add(scheme);

        Should.Throw<FluentCmsConfigurationException>(options.Validate).Message.ShouldContain(nameof(RichTextOptions.LinkSchemes));
    }

    [Fact]
    public void TheSchemesReplaceTheDefault()
    {
        var options = new RichTextOptions();
        options.LinkSchemes.Add("sms");
        options.LinkSchemes.Add("https");
        options.Validate();

        options.Links.IsSafe("sms:+15551234567").ShouldBeTrue();
        options.Links.IsSafe("mailto:someone@example.com").ShouldBeFalse();
        new RichTextOptions().Links.IsSafe("sms:+15551234567").ShouldBeFalse();
        new RichTextOptions().Links.IsSafe("mailto:someone@example.com").ShouldBeTrue();
    }

    [Fact]
    public void TheRenderersUseTheSchemesOfTheOptions()
    {
        var options = new RichTextOptions();
        options.LinkSchemes.Add("sms");
        var document = Paragraph("""{"type":"text","text":"Text","marks":[{"type":"link","attrs":{"href":"sms:+1"}}]}""");

        new RichTextRenderer(options: options).Render(document, RichTextFormats.Html).ShouldBe("<p><a href=\"sms:+1\">Text</a></p>");
        new RichTextRenderer(options: options).Render(document, RichTextFormats.Markdown).ShouldBe("[Text](sms:+1)");
        new RichTextRenderer().Render(document, RichTextFormats.Html).ShouldBe("<p>Text</p>");
    }

    [Fact]
    public void AReadDropsAnUnsafeUrlFromAnyAttributeAndKeepsTheRest()
    {
        var document = Paragraph(
            """{"type":"text","text":"A","marks":[{"type":"bold"},{"type":"link","attrs":{"href":"javascript:alert(1)"}}]},""" +
            """{"type":"text","text":"B","marks":[{"type":"entryLink","attrs":{"entryId":"11111111-1111-1111-1111-111111111111","entryType":"article","href":"JAVASCRIPT:x"}}]},""" +
            """{"type":"media","attrs":{"mediaId":"22222222-2222-2222-2222-222222222222","src":"data:text/html,x","alt":"Kept"}},""" +
            """{"type":"inlineBlock","attrs":{"blockType":"gallery","data":{"items":[{"url":"vbscript:x","label":"Kept"}]}}},""" +
            """{"type":"inlineBlock","attrs":{"blockType":"card","data":{"note":{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"C","marks":[{"type":"link","attrs":{"href":"javascript:y"}}]}]}]}}}}""");

        var read = HiddenFields.DropUnsafeLinks(Schema, document, SafeUrl.Default).Json;

        read.ShouldNotContain("script:", Case.Insensitive);
        read.ShouldNotContain("data:");
        JsonNode.Parse(read)!["content"]![0]!["content"]![0]!["marks"]!.AsArray().ShouldHaveSingleItem()!["type"]!.GetValue<string>().ShouldBe("bold");
        read.ShouldContain("entryLink");
        read.ShouldContain("Kept");
    }

    [Theory]
    [InlineData("""{"type":"doc","content":[]}""", false)]
    [InlineData("""{"attrs":{"href":"https://example.com/x"}}""", false)]
    [InlineData("""{"attrs":{"href": "/relative"}}""", false)]
    [InlineData("""{"attrs":{"href":"javascript:x"}}""", true)]
    [InlineData("""{"attrs":{"HREF": "javascript:x"}}""", true)]
    [InlineData("""{"attrs":{"src":"data:x"}}""", true)]
    [InlineData("""{"attrs":{"href":null}}""", false)]
    [InlineData("""{"attrs":{"href": ["javascript:x"]}}""", true)]
    [InlineData("""{"attrs":{"href":5}}""", true)]
    [InlineData("""{"attrs":{"src":{"value":"x"}}}""", true)]
    [InlineData("""{"title":"href"}""", false)]
    [InlineData("""{"attrs":{"href":"java\tscript:x"}}""", true)]
    [InlineData("{\"attrs\":{\"\\u0068ref\":\"javascript:x\"}}", true)]
    [InlineData("{\"text\":\"a \\u0026 b\"}", false)]
    public void APreScanFindsEveryUrlThatCouldBeUnsafe(string json, bool may) =>
        RichTextLinks.MayHoldUnsafe(json, SafeUrl.Default).ShouldBe(may);

    [Fact]
    public void AReadDropsAValueThatIsNotAStringAndAUrlInsideAnAttribute()
    {
        var document = Paragraph(
            """{"type":"text","text":"A","marks":[{"type":"entryLink","attrs":{"entryId":"11111111-1111-1111-1111-111111111111","entryType":"article","href":["javascript:x"]}}]},""" +
            """{"type":"media","attrs":{"mediaId":"22222222-2222-2222-2222-222222222222","src":5,"link":{"href":"javascript:y","label":"Kept"}}},""" +
            """{"type":"inlineBlock","attrs":{"blockType":"gallery","data":{"url":["javascript:z"],"items":[{"src":{"value":"x"}}]}}}""");

        var read = HiddenFields.DropUnsafeLinks(Schema, document, SafeUrl.Default).Json;

        read.ShouldNotContain("javascript");
        read.ShouldNotContain("\"src\"");
        read.ShouldNotContain("\"url\"");
        read.ShouldContain("Kept");
        read.ShouldContain("entryLink");
    }

    [Fact]
    public void ADocumentWithOnlySafeUrlsIsReturnedAsItWas()
    {
        var document = Paragraph("""{"type":"text","text":"A","marks":[{"type":"link","attrs":{"href":"https://example.com"}}]}""");

        HiddenFields.DropUnsafeLinks(Schema, document, SafeUrl.Default).ShouldBeSameAs(document);
    }
}
