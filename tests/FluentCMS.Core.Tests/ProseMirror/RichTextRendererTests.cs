using System.Net;
using FluentCMS.Core.ProseMirror;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.ProseMirror;

public class RichTextRendererTests
{
    private static readonly RichTextRenderOptions resolving = new()
    {
        EntryUrlResolver = (type, id) => $"/{type}/{id}",
        MediaUrlResolver = id => $"https://cdn.example.com/media/{id}.png",
    };

    private static readonly RichTextRenderer renderer = new();

    public static TheoryData<string, string, bool, string> SnapshotCases()
    {
        var cases = new TheoryData<string, string, bool, string>();
        foreach (var document in new[] { "article", "marks" })
        {
            cases.Add(document, RichTextFormats.Html, false, $"{document}.html");
            cases.Add(document, RichTextFormats.Html, true, $"{document}.resolved.html");
            cases.Add(document, RichTextFormats.Markdown, false, $"{document}.markdown");
            cases.Add(document, RichTextFormats.Markdown, true, $"{document}.resolved.markdown");
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(SnapshotCases))]
    public void RendersTheSnapshot(string document, string format, bool resolve, string snapshot)
    {
        var value = new RichText(Snapshot.Read($"{document}.json"));

        var output = renderer.Render(value, format, resolve ? resolving : null);

        output.ShouldMatchSnapshot(snapshot);
    }

    [Fact]
    public void OutputIsStable()
    {
        var value = new RichText(Snapshot.Read("article.json"));

        renderer.Render(value, RichTextFormats.Html).ShouldBe(renderer.Render(value, RichTextFormats.Html));
        renderer.Render(value, RichTextFormats.Markdown).ShouldBe(renderer.Render(value, RichTextFormats.Markdown));
    }

    [Fact]
    public void ListsTheSupportedFormats()
    {
        renderer.Formats.ShouldBe([RichTextFormats.Html, RichTextFormats.Markdown], ignoreOrder: true);
    }

    [Fact]
    public void FormatNamesIgnoreCase()
    {
        renderer.Render(RichText.Empty, "HTML").ShouldBe("<p></p>");
        renderer.Render(RichText.Empty, "Markdown").ShouldBe(string.Empty);
    }

    [Fact]
    public void RejectsAnUnsupportedFormat()
    {
        var exception = Should.Throw<ArgumentException>(() => renderer.Render(RichText.Empty, "pdf"));

        exception.ParamName.ShouldBe("format");
        exception.Message.ShouldContain("html");
    }

    [Fact]
    public void RejectsAnInvalidDocumentInsteadOfDroppingNodes()
    {
        var value = new RichText("""{"type":"doc","content":[{"type":"paragraph"},{"type":"table"}]}""");

        var exception = Should.Throw<RichTextValidationException>(() => renderer.Render(value, RichTextFormats.Html));

        exception.Errors.ShouldHaveSingleItem().Path.ShouldBe("$.content[1]");
    }

    [Fact]
    public void RendersADocumentBuiltInCode()
    {
        var document = new DocumentNode(
        [
            new HeadingNode(2, [new TextNode("Hello")]),
            new ParagraphNode([new TextNode("World", [new BoldMark()])]),
        ]);

        renderer.Render(document, RichTextFormats.Html).ShouldBe("<h2>Hello</h2>\n<p><strong>World</strong></p>");
        renderer.Render(document, RichTextFormats.Markdown).ShouldBe("## Hello\n\n**World**");
    }

    [Fact]
    public void RejectsABuiltInDocumentThatBreaksTheStructure()
    {
        var document = new DocumentNode([new TextNode("loose")]);

        Should.Throw<RichTextValidationException>(() => renderer.Render(document, RichTextFormats.Html));
    }

    [Fact]
    public void EncodesTextAndAttributes()
    {
        var value = new RichText("""
            {"type":"doc","content":[{"type":"paragraph","content":[
              {"type":"text","text":"<script>alert(\"x\")</script> & more"},
              {"type":"text","marks":[{"type":"link","attrs":{"href":"https://example.com/?a=1&b=\"2\"","title":"\" onmouseover=\"alert(1)"}}],"text":"link"}
            ]}]}
            """);

        var html = renderer.Render(value, RichTextFormats.Html);

        html.ShouldBe(
            "<p>&lt;script&gt;alert(\"x\")&lt;/script&gt; &amp; more" +
            "<a href=\"https://example.com/?a=1&amp;b=&quot;2&quot;\" title=\"&quot; onmouseover=&quot;alert(1)\">link</a></p>");
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData("  javascript:alert(1)")]
    [InlineData("java\tscript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("javascript&#58;alert(1)")]
    [InlineData("javascript&#x3A;alert(1)")]
    [InlineData("javascript&colon;alert(1)")]
    [InlineData("&#106;avascript:alert(1)")]
    public void DropsLinksWithUnsafeSchemes(string href)
    {
        var value = Linked(href);

        renderer.Render(value, RichTextFormats.Html).ShouldBe("<p>text</p>");
        renderer.Render(value, RichTextFormats.Markdown).ShouldBe("text");
    }

    [Fact]
    public void MarkdownEncodesAmpersandsInDestinationsAndTitles()
    {
        var entry = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var media = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var value = RichTextSerializer.ToRichText(new DocumentNode(
        [
            new ParagraphNode(
            [
                new TextNode("x", [new LinkMark("https://example.com/?a=1&b=2&amp;c", title: "A & B")]),
                new TextNode("e", [new EntryLinkMark(entry, "article")]),
            ]),
            new MediaNode(media),
        ]));
        var options = new RichTextRenderOptions
        {
            EntryUrlResolver = (_, _) => "/e?a=1&b=2",
            MediaUrlResolver = _ => "https://cdn.example.com/m.png?w=1&h=2",
        };

        renderer.Render(value, RichTextFormats.Markdown, options).ShouldBe(
            "[x](https://example.com/?a=1&amp;b=2&amp;amp;c \"A &amp; B\")[e](/e?a=1&amp;b=2)\n\n" +
            "![](https://cdn.example.com/m.png?w=1&amp;h=2)");
    }

    [Theory]
    [InlineData("foo", "===", "foo\\\n\\===")]
    [InlineData("foo", "= = =", "foo\\\n\\= = =")]
    [InlineData("foo", "=== bar", "foo\\\n=== bar")]
    [InlineData("foo", "    indented", "foo\\\nindented")]
    [InlineData("foo", "   # not a heading", "foo\\\n\\# not a heading")]
    public void MarkdownKeepsTextAfterAHardBreakFromChangingTheBlock(string first, string second, string expected)
    {
        var value = RichTextSerializer.ToRichText(new DocumentNode(
            [new ParagraphNode([new TextNode(first), new HardBreakNode(), new TextNode(second)])]));

        renderer.Render(value, RichTextFormats.Markdown).ShouldBe(expected);
    }

    [Theory]
    [InlineData("    indented", "indented")]
    [InlineData("\tindented", "indented")]
    [InlineData("a\n    b", "a\nb")]
    [InlineData("   - not a bullet", "\\- not a bullet")]
    [InlineData("a  b", "a  b")]
    public void MarkdownDropsIndentationAtTheStartOfALine(string text, string expected)
    {
        var value = RichTextSerializer.ToRichText(new DocumentNode([new ParagraphNode([new TextNode(text)])]));

        renderer.Render(value, RichTextFormats.Markdown).ShouldBe(expected);
    }

    [Fact]
    public void MarkdownDropsIndentationBeforeAMarkedRun()
    {
        var value = RichTextSerializer.ToRichText(new DocumentNode(
            [new ParagraphNode([new TextNode("    bold", [new BoldMark()])])]));

        renderer.Render(value, RichTextFormats.Markdown).ShouldBe("**bold**");
    }

    [Fact]
    public void MarkdownNumbersAListFromItsStart()
    {
        var items = new[] { new ListItemNode([new ParagraphNode([new TextNode("a")])]), new ListItemNode([new ParagraphNode([new TextNode("b")])]) };
        var value = RichTextSerializer.ToRichText(new DocumentNode([new OrderedListNode(items, start: 0)]));

        renderer.Render(value, RichTextFormats.Markdown).ShouldBe("0. a\n1. b");
    }

    [Theory]
    [InlineData("https://example.com/a")]
    [InlineData("HTTP://example.com")]
    [InlineData("mailto:a@example.com")]
    [InlineData("tel:+123")]
    [InlineData("/relative/path")]
    [InlineData("relative/path")]
    [InlineData("#anchor")]
    [InlineData("?query=1")]
    [InlineData("//cdn.example.com/x")]
    public void KeepsLinksWithSafeTargets(string href)
    {
        renderer.Render(Linked(href), RichTextFormats.Html).ShouldContain("<a href=");
    }

    [Fact]
    public void OnlyOpensLinksInANewTabSafely()
    {
        var value = new RichText("""
            {"type":"doc","content":[{"type":"paragraph","content":[
              {"type":"text","marks":[{"type":"link","attrs":{"href":"/a","target":"_blank","rel":"nofollow","class":"x"}}],"text":"a"},
              {"type":"text","marks":[{"type":"link","attrs":{"href":"/b","target":"_top"}}],"text":"b"}
            ]}]}
            """);

        renderer.Render(value, RichTextFormats.Html).ShouldBe(
            "<p><a href=\"/a\" target=\"_blank\" rel=\"noopener noreferrer\">a</a><a href=\"/b\">b</a></p>");
    }

    [Fact]
    public void IgnoresUnsafeUrlsFromResolvers()
    {
        var value = new RichText("""
            {"type":"doc","content":[
              {"type":"paragraph","content":[{"type":"text","marks":[{"type":"entryLink","attrs":{"entryId":"11111111-1111-1111-1111-111111111111","entryType":"article"}}],"text":"entry"}]},
              {"type":"media","attrs":{"mediaId":"22222222-2222-2222-2222-222222222222"}}
            ]}
            """);
        var options = new RichTextRenderOptions
        {
            EntryUrlResolver = (_, _) => "javascript:alert(1)",
            MediaUrlResolver = _ => "javascript:alert(1)",
        };

        renderer.Render(value, RichTextFormats.Html, options).ShouldBe(
            "<p><a data-entry-type=\"article\" data-entry-id=\"11111111-1111-1111-1111-111111111111\">entry</a></p>\n" +
            "<figure><img alt=\"\" data-media-id=\"22222222-2222-2222-2222-222222222222\"></figure>");
    }

    [Fact]
    public void KeepsPlaceholdersWhenAResolverReturnsNull()
    {
        var value = new RichText("""
            {"type":"doc","content":[{"type":"media","attrs":{"mediaId":"22222222-2222-2222-2222-222222222222"}}]}
            """);
        var options = new RichTextRenderOptions { MediaUrlResolver = _ => null };

        renderer.Render(value, RichTextFormats.Markdown, options).ShouldBe("![](fcms:media/22222222-2222-2222-2222-222222222222)");
    }

    [Fact]
    public void ACustomRendererChangesTheOutputOfATypedInlineBlock()
    {
        var custom = new RichTextRenderer([new CalloutRenderer()]);
        var value = new RichText(Snapshot.Read("article.json"));

        var html = custom.Render(value, RichTextFormats.Html);
        var markdown = custom.Render(value, RichTextFormats.Markdown);

        html.ShouldContain("<mark data-tone=\"info\">Heads up</mark>");
        html.ShouldContain("<aside data-tone=\"warning\">Block level</aside>");
        html.ShouldNotContain("data-fcms-block");
        markdown.ShouldContain("**Heads up**");
        markdown.ShouldContain("> Block level");
        markdown.ShouldNotContain("fcms-block");
    }

    [Fact]
    public void ACustomRendererCanDeclineAFormat()
    {
        var custom = new RichTextRenderer([new CalloutRenderer(declineMarkdown: true)]);
        var value = new RichText(Snapshot.Read("article.json"));

        custom.Render(value, RichTextFormats.Html).ShouldContain("<aside");
        custom.Render(value, RichTextFormats.Markdown).ShouldContain("<!-- fcms-block: callout -->");
    }

    [Fact]
    public void ACustomRendererCanReplaceABuiltInNodeAndRenderItsChildren()
    {
        var custom = new RichTextRenderer([new HeadingRenderer()]);
        var value = new RichText("""
            {"type":"doc","content":[{"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Hi "},{"type":"text","marks":[{"type":"bold"}],"text":"there"}]}]}
            """);

        custom.Render(value, RichTextFormats.Html).ShouldBe("<div class=\"title level-2\">Hi <strong>there</strong></div>");
        custom.Render(value, RichTextFormats.Markdown).ShouldBe("TITLE Hi **there**");
    }

    [Fact]
    public void ABlockTypeRendererDoesNotTouchABuiltInNodeOfTheSameName()
    {
        var custom = new RichTextRenderer([new CalloutRenderer()]);

        custom.Render(RichText.Empty, RichTextFormats.Html).ShouldBe("<p></p>");
    }

    [Fact]
    public void RejectsRenderersThatCannotBeRegistered()
    {
        Should.Throw<ArgumentException>(() => new RichTextRenderer([new NamedRenderer("callout"), new NamedRenderer("callout")]));
        Should.Throw<ArgumentException>(() => new RichTextRenderer([new NamedRenderer("")]));
        Should.Throw<ArgumentException>(() => new RichTextRenderer([new NamedRenderer(RichTextNodeTypes.Text)]));
        Should.Throw<ArgumentException>(() => new RichTextRenderer([new NamedRenderer(RichTextNodeTypes.Doc)]));
        Should.Throw<ArgumentException>(() => new RichTextRenderer([new NamedRenderer(RichTextNodeTypes.InlineBlock)]));
    }

    private static RichText Linked(string href) =>
        new(RichTextSerializer.Serialize(new DocumentNode([new ParagraphNode([new TextNode("text", [new LinkMark(href)])])])));

    private sealed class NamedRenderer(string nodeType) : IRichTextNodeRenderer
    {
        public string NodeType { get; } = nodeType;

        public string? Render(IRichTextNodeContext context) => null;
    }

    private sealed class CalloutRenderer(bool declineMarkdown = false) : IRichTextNodeRenderer
    {
        public string NodeType => "callout";

        public string? Render(IRichTextNodeContext context)
        {
            var tone = context.Attributes["tone"].GetString();
            var text = context.Attributes["text"].GetString()!;

            return context.Format switch
            {
                RichTextFormats.Html when context.IsInline => $"<mark data-tone=\"{tone}\">{WebUtility.HtmlEncode(text)}</mark>",
                RichTextFormats.Html => $"<aside data-tone=\"{tone}\">{WebUtility.HtmlEncode(text)}</aside>",
                RichTextFormats.Markdown when declineMarkdown => null,
                RichTextFormats.Markdown when context.IsInline => $"**{text}**",
                RichTextFormats.Markdown => $"> {text}",
                _ => null,
            };
        }
    }

    private sealed class HeadingRenderer : IRichTextNodeRenderer
    {
        public string NodeType => RichTextNodeTypes.Heading;

        public string? Render(IRichTextNodeContext context) =>
            context.Format == RichTextFormats.Html
                ? $"<div class=\"title level-{context.Attributes["level"].GetInt32()}\">{context.RenderChildren()}</div>"
                : $"TITLE {context.RenderChildren()}";
    }
}
