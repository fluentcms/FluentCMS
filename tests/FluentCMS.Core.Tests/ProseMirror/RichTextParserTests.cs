using FluentCMS.Core.ProseMirror;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.ProseMirror;

public class RichTextParserTests
{
    [Theory]
    [InlineData("article.json")]
    [InlineData("marks.json")]
    public void RoundTripIsLossless(string snapshot)
    {
        var json = Snapshot.Compact(Snapshot.Read(snapshot));

        var document = RichTextParser.Parse(json);

        RichTextSerializer.Serialize(document).ShouldBe(json);
        RichTextSerializer.ToRichText(document).Json.ShouldBe(json);
    }

    [Fact]
    public void TheEmptyDocumentRoundTrips()
    {
        var document = RichTextParser.Parse(RichText.Empty);

        document.Content.ShouldHaveSingleItem().ShouldBeOfType<ParagraphNode>();
        RichTextSerializer.ToRichText(document).ShouldBe(RichText.Empty);
    }

    [Fact]
    public void WritesTheCanonicalKeyOrderWhateverTheInputOrder()
    {
        const string input = """
            {"content":[{"content":[{"text":"Hi","marks":[{"attrs":{"href":"/a","title":"T"},"type":"link"}],"type":"text"}],"type":"paragraph"}],"type":"doc"}
            """;

        RichTextSerializer.Serialize(RichTextParser.Parse(input)).ShouldBe(
            """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","marks":[{"type":"link","attrs":{"href":"/a","title":"T"}}],"text":"Hi"}]}]}""");
    }

    [Fact]
    public void KeepsAttributesTheModelDoesNotInterpret()
    {
        const string json = """
            {"type":"doc","content":[{"type":"heading","attrs":{"level":2,"textAlign":"center"},"content":[{"type":"text","marks":[{"type":"link","attrs":{"class":null,"href":"/a","rel":"nofollow"}}],"text":"Hi"}]}]}
            """;

        RichTextSerializer.Serialize(RichTextParser.Parse(json)).ShouldBe(json);
    }

    [Fact]
    public void KeepsNumbersAndNestedBlockDataAsWritten()
    {
        const string json = """
            {"type":"doc","content":[{"type":"inlineBlock","attrs":{"blockType":"chart","data":{"values":[1,2.50,1e3],"nested":{"b":true,"a":null}}}}]}
            """;

        RichTextSerializer.Serialize(RichTextParser.Parse(json)).ShouldBe(json);
    }

    [Fact]
    public void ParsesTheTypedValues()
    {
        var document = RichTextParser.Parse(Snapshot.Read("article.json"));

        document.Content[0].ShouldBeOfType<HeadingNode>().Level.ShouldBe(1);
        document.Content[6].ShouldBeOfType<OrderedListNode>().Start.ShouldBe(1);
        document.Content[5].Content[1].Content[1].ShouldBeOfType<OrderedListNode>().Start.ShouldBe(3);

        var code = document.Content[8].ShouldBeOfType<CodeBlockNode>();
        code.Language.ShouldBe("csharp");
        code.Code.ShouldBe("var small = 1 < 2;\nConsole.WriteLine(small);");
        document.Content[9].ShouldBeOfType<CodeBlockNode>().Language.ShouldBeNull();

        var media = document.Content[11].ShouldBeOfType<MediaNode>();
        media.MediaId.ShouldBe(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        media.Alt.ShouldBe("A diagram");
        media.Title.ShouldBe("Architecture");

        var links = document.Content[3].Content;
        var link = links[1].Marks.ShouldHaveSingleItem().ShouldBeOfType<LinkMark>();
        link.Href.ShouldBe("https://example.com/docs?a=1&b=2");
        link.Target.ShouldBe("_blank");
        var entry = links[3].Marks.ShouldHaveSingleItem().ShouldBeOfType<EntryLinkMark>();
        entry.EntryId.ShouldBe(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        entry.EntryType.ShouldBe("article");

        var block = document.Content[13].ShouldBeOfType<InlineBlockNode>();
        block.BlockType.ShouldBe("callout");
        block.Data["tone"].GetString().ShouldBe("warning");
    }

    [Fact]
    public void AcceptsAnEmptyDocumentAndEmptyContentArrays()
    {
        RichTextParser.Parse("""{"type":"doc"}""").Content.ShouldBeEmpty();

        RichTextSerializer.Serialize(RichTextParser.Parse("""{"type":"doc","content":[{"type":"horizontalRule","content":[]}]}"""))
            .ShouldBe("""{"type":"doc","content":[{"type":"horizontalRule"}]}""");
    }

    [Theory]
    [InlineData("{", "$", "not valid JSON")]
    [InlineData("", "$", "not valid JSON")]
    [InlineData("[]", "$", "Expected an object")]
    [InlineData("""{"type":"paragraph"}""", "$", "root node must be of type 'doc'")]
    [InlineData("""{"type":"doc","content":[{}]}""", "$.content[0]", "no type")]
    [InlineData("""{"type":"doc","content":[{"type":5}]}""", "$.content[0].type", "must be a string")]
    [InlineData("""{"type":"doc","content":{}}""", "$.content", "must be an array")]
    [InlineData("""{"type":"doc","content":["x"]}""", "$.content[0]", "Expected an object")]
    [InlineData("""{"type":"doc","content":[{"type":"paragraph","extra":1}]}""", "$.content[0]", "Unknown property 'extra'")]
    [InlineData("""{"type":"doc","type":"doc"}""", "$", "not valid JSON")]
    [InlineData("""{"type":"doc","content":[{"type":"paragraph","attrs":[]}]}""", "$.content[0].attrs", "must be an object")]
    [InlineData("""{"type":"doc","content":[{"type":"heading","content":[]}]}""", "$.content[0].attrs.level", "between 1 and 6")]
    [InlineData("""{"type":"doc","content":[{"type":"heading","attrs":{"level":7}}]}""", "$.content[0].attrs.level", "between 1 and 6")]
    [InlineData("""{"type":"doc","content":[{"type":"heading","attrs":{"level":"2"}}]}""", "$.content[0].attrs.level", "between 1 and 6")]
    [InlineData("""{"type":"doc","content":[{"type":"orderedList","attrs":{"start":"a"}}]}""", "$.content[0].attrs.start", "must be an integer")]
    [InlineData("""{"type":"doc","content":[{"type":"orderedList","attrs":{"start":-3}}]}""", "$.content[0].attrs.start", "between 0 and 999999999")]
    [InlineData("""{"type":"doc","content":[{"type":"orderedList","attrs":{"start":1000000000}}]}""", "$.content[0].attrs.start", "between 0 and 999999999")]
    [InlineData("""{"type":"doc","content":[{"type":"codeBlock","attrs":{"language":3}}]}""", "$.content[0].attrs.language", "must be a string")]
    [InlineData("""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text"}]}]}""", "$.content[0].content[0].text", "non-empty string")]
    [InlineData("""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":""}]}]}""", "$.content[0].content[0].text", "non-empty string")]
    [InlineData("""{"type":"doc","content":[{"type":"paragraph","text":"x"}]}""", "$.content[0].text", "cannot have text")]
    [InlineData("""{"type":"doc","content":[{"type":"horizontalRule","content":[{"type":"paragraph"}]}]}""", "$.content[0].content", "cannot have content")]
    [InlineData("""{"type":"doc","content":[{"type":"paragraph","marks":[{"type":"bold"}]}]}""", "$.content[0].marks", "cannot have marks")]
    [InlineData("""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"a","marks":{}}]}]}""", "$.content[0].content[0].marks", "must be an array")]
    [InlineData("""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"a","marks":[{"type":"link"}]}]}]}""", "$.content[0].content[0].marks[0].attrs.href", "must be a string")]
    [InlineData("""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"a","marks":[{"type":"link","attrs":{"href":"/a","title":4}}]}]}]}""", "$.content[0].content[0].marks[0].attrs.title", "must be a string")]
    [InlineData("""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"a","marks":[{"type":"entryLink","attrs":{"entryId":"x","entryType":"article"}}]}]}]}""", "$.content[0].content[0].marks[0].attrs.entryId", "must be a GUID")]
    [InlineData("""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"a","marks":[{"type":"entryLink","attrs":{"entryId":"11111111-1111-1111-1111-111111111111","entryType":"Article"}}]}]}]}""", "$.content[0].content[0].marks[0].attrs.entryType", "kebab-case")]
    [InlineData("""{"type":"doc","content":[{"type":"media","attrs":{"mediaId":"nope"}}]}""", "$.content[0].attrs.mediaId", "must be a GUID")]
    [InlineData("""{"type":"doc","content":[{"type":"media"}]}""", "$.content[0].attrs.mediaId", "must be a GUID")]
    [InlineData("""{"type":"doc","content":[{"type":"inlineBlock","attrs":{"blockType":"paragraph"}}]}""", "$.content[0].attrs.blockType", "reserved")]
    [InlineData("""{"type":"doc","content":[{"type":"inlineBlock","attrs":{"blockType":"two words"}}]}""", "$.content[0].attrs.blockType", "must start with a letter")]
    [InlineData("""{"type":"doc","content":[{"type":"inlineBlock"}]}""", "$.content[0].attrs.blockType", "must be a string")]
    [InlineData("""{"type":"doc","content":[{"type":"inlineBlock","attrs":{"blockType":"callout","data":[]}}]}""", "$.content[0].attrs.data", "must be an object")]
    public void ReportsInvalidDocumentsWithThePath(string json, string path, string message)
    {
        RichTextParser.TryParse(json, out var document, out var errors).ShouldBeFalse();

        document.ShouldBeNull();
        errors.ShouldContain(error => error.Path == path && error.Message.Contains(message, StringComparison.Ordinal));
    }

    [Fact]
    public void ParseThrowsWithEveryError()
    {
        const string json = """{"type":"doc","content":[{"type":"heading","attrs":{"level":9}},{"type":"horizontalRule","marks":[{"type":"bold"}]}]}""";

        var exception = Should.Throw<RichTextValidationException>(() => RichTextParser.Parse(json));

        exception.ShouldBeAssignableTo<FormatException>();
        exception.Errors.Select(error => error.Path).ShouldBe(["$.content[0].attrs.level", "$.content[1].marks"]);
        exception.Message.ShouldContain("$.content[0].attrs.level");
    }

    [Fact]
    public void RejectsDeeplyNestedJsonInsteadOfOverflowingTheStack()
    {
        var json = string.Concat(Enumerable.Repeat("""{"type":"blockquote","content":[""", 200)) + string.Concat(Enumerable.Repeat("]}", 200));

        RichTextParser.TryParse($$"""{"type":"doc","content":[{{json}}]}""", out _, out var errors).ShouldBeFalse();

        errors.ShouldHaveSingleItem().Path.ShouldBe("$");
    }
}
