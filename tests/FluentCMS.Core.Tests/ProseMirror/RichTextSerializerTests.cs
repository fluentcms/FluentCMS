using System.Text.Json;
using FluentCMS.Core.ProseMirror;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.ProseMirror;

public class RichTextSerializerTests
{
    [Fact]
    public void SerializesABuiltInDocument()
    {
        var entry = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var media = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var data = new Dictionary<string, JsonElement> { ["tone"] = JsonSerializer.SerializeToElement("info") };

        var document = new DocumentNode(
        [
            new HeadingNode(2, [new TextNode("Title")]),
            new ParagraphNode(
            [
                new TextNode("see "),
                new TextNode("guide", [new EntryLinkMark(entry, "article"), new BoldMark()]),
                new HardBreakNode(),
                new InlineBlockNode("callout", data),
            ]),
            new OrderedListNode([new ListItemNode([new ParagraphNode([new TextNode("one")])])], start: 4),
            new CodeBlockNode("x < y", "csharp"),
            new HorizontalRuleNode(),
            new MediaNode(media, "Alt", "Title"),
        ]);

        RichTextSerializer.Serialize(document).ShouldBe(
            """{"type":"doc","content":[""" +
            """{"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Title"}]},""" +
            """{"type":"paragraph","content":[{"type":"text","text":"see "},""" +
            """{"type":"text","marks":[{"type":"entryLink","attrs":{"entryId":"11111111-1111-1111-1111-111111111111","entryType":"article"}},{"type":"bold"}],"text":"guide"},""" +
            """{"type":"hardBreak"},{"type":"inlineBlock","attrs":{"blockType":"callout","data":{"tone":"info"}}}]},""" +
            """{"type":"orderedList","attrs":{"start":4},"content":[{"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"one"}]}]}]},""" +
            """{"type":"codeBlock","attrs":{"language":"csharp"},"content":[{"type":"text","text":"x < y"}]},""" +
            """{"type":"horizontalRule"},""" +
            """{"type":"media","attrs":{"alt":"Alt","mediaId":"22222222-2222-2222-2222-222222222222","title":"Title"}}]}""");
    }

    [Fact]
    public void ABuiltInDocumentSurvivesAParse()
    {
        var document = new DocumentNode([new ParagraphNode([new TextNode("é — ünï", [new ItalicMark()])])]);

        var json = RichTextSerializer.Serialize(document);

        RichTextSerializer.Serialize(RichTextParser.Parse(json)).ShouldBe(json);
        json.ShouldContain("é — ünï");
    }

    [Fact]
    public void ConstructorsRefuseValuesTheParserWouldReject()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new HeadingNode(0));
        Should.Throw<ArgumentOutOfRangeException>(() => new HeadingNode(7));
        Should.Throw<ArgumentOutOfRangeException>(() => new OrderedListNode(null, -1));
        Should.Throw<ArgumentOutOfRangeException>(() => new OrderedListNode(null, 1_000_000_000));
        Should.Throw<ArgumentException>(() => new InlineBlockNode("paragraph"));
        Should.Throw<ArgumentException>(() => new InlineBlockNode("not valid"));
        Should.Throw<ArgumentException>(() => new EntryLinkMark(Guid.NewGuid(), "Not Kebab"));
        Should.Throw<ArgumentException>(() => new TextNode(string.Empty));
    }
}
