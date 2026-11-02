using FluentCMS.Core.ProseMirror;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.ProseMirror;

public class RichTextValidatorTests
{
    [Theory]
    [InlineData("article.json")]
    [InlineData("marks.json")]
    public void AcceptsTheRepresentativeDocuments(string snapshot)
    {
        RichTextValidator.Validate(new RichText(Snapshot.Read(snapshot))).ShouldBeEmpty();
    }

    [Fact]
    public void AcceptsTheEmptyDocument()
    {
        RichTextValidator.Validate(RichText.Empty).ShouldBeEmpty();
    }

    [Fact]
    public void RejectsAnUnknownNodeTypeAndNamesThePath()
    {
        var value = new RichText("""
            {"type":"doc","content":[
              {"type":"paragraph","content":[{"type":"text","text":"ok"}]},
              {"type":"blockquote","content":[{"type":"table"}]}
            ]}
            """);

        var error = RichTextValidator.Validate(value).ShouldHaveSingleItem();

        error.Path.ShouldBe("$.content[1].content[0]");
        error.Message.ShouldBe("Unknown node type 'table'.");
        Should.Throw<RichTextValidationException>(() => RichTextParser.Parse(value));
    }

    [Fact]
    public void RejectsAnUnknownMarkAndNamesThePath()
    {
        var value = new RichText("""
            {"type":"doc","content":[{"type":"paragraph","content":[
              {"type":"text","text":"ok"},
              {"type":"text","text":"hi","marks":[{"type":"bold"},{"type":"highlight"}]}
            ]}]}
            """);

        var error = RichTextValidator.Validate(value).ShouldHaveSingleItem();

        error.Path.ShouldBe("$.content[0].content[1].marks[1]");
        error.Message.ShouldBe("Unknown mark type 'highlight'.");
    }

    [Fact]
    public void ReportsEveryUnknownNodeInOnePass()
    {
        var value = new RichText("""
            {"type":"doc","content":[{"type":"table"},{"type":"paragraph"},{"type":"video"}]}
            """);

        RichTextValidator.Validate(value).Select(error => error.Path).ShouldBe(["$.content[0]", "$.content[2]"]);
    }

    [Theory]
    [InlineData("""{"type":"doc","content":[{"type":"text","text":"loose"}]}""", "$.content[0]", "'text' node is not allowed inside a 'doc'")]
    [InlineData("""{"type":"doc","content":[{"type":"listItem"}]}""", "$.content[0]", "'listItem' node is not allowed inside a 'doc'")]
    [InlineData("""{"type":"doc","content":[{"type":"hardBreak"}]}""", "$.content[0]", "'hardBreak' node is not allowed inside a 'doc'")]
    [InlineData("""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"paragraph"}]}]}""", "$.content[0].content[0]", "'paragraph' node is not allowed inside a 'paragraph'")]
    [InlineData("""{"type":"doc","content":[{"type":"heading","attrs":{"level":1},"content":[{"type":"bulletList"}]}]}""", "$.content[0].content[0]", "not allowed inside a 'heading'")]
    [InlineData("""{"type":"doc","content":[{"type":"bulletList","content":[{"type":"paragraph"}]}]}""", "$.content[0].content[0]", "not allowed inside a 'bulletList'")]
    [InlineData("""{"type":"doc","content":[{"type":"codeBlock","content":[{"type":"hardBreak"}]}]}""", "$.content[0].content[0]", "not allowed inside a 'codeBlock'")]
    [InlineData("""{"type":"doc","content":[{"type":"codeBlock","content":[{"type":"text","text":"x","marks":[{"type":"bold"}]}]}]}""", "$.content[0].content[0].marks", "marks apply to inline content only")]
    [InlineData("""{"type":"doc","content":[{"type":"media","attrs":{"mediaId":"22222222-2222-2222-2222-222222222222"},"marks":[{"type":"bold"}]}]}""", "$.content[0].marks", "marks apply to inline content only")]
    [InlineData("""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"x","marks":[{"type":"bold"},{"type":"bold"}]}]}]}""", "$.content[0].content[0].marks[1]", "'bold' mark appears more than once")]
    public void RejectsContentThatBreaksTheStructure(string json, string path, string message)
    {
        var errors = RichTextValidator.Validate(new RichText(json));

        errors.ShouldContain(error => error.Path == path && error.Message.Contains(message, StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatesABuiltInDocument()
    {
        var invalid = new DocumentNode([new ParagraphNode([new ParagraphNode()])]);
        var valid = new DocumentNode([new ParagraphNode([new TextNode("fine")])]);

        RichTextValidator.Validate(invalid).ShouldHaveSingleItem().Path.ShouldBe("$.content[0].content[0]");
        RichTextValidator.Validate(valid).ShouldBeEmpty();
    }

    [Fact]
    public void InlineNodesMayAppearBetweenBlocks()
    {
        var value = new RichText("""
            {"type":"doc","content":[
              {"type":"media","attrs":{"mediaId":"22222222-2222-2222-2222-222222222222"}},
              {"type":"inlineBlock","attrs":{"blockType":"callout"}}
            ]}
            """);

        RichTextValidator.Validate(value).ShouldBeEmpty();
    }
}
