using FluentCMS.Core.ProseMirror;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.ProseMirror;

public class RichTextReferenceExtractorTests
{
    [Fact]
    public void ReturnsEntryAndMediaIdsWithTheirPaths()
    {
        var references = RichTextReferenceExtractor.Extract(new RichText(Snapshot.Read("article.json")));

        references.Entries.ShouldBe(
        [
            new RichTextEntryReference(Guid.Parse("11111111-1111-1111-1111-111111111111"), "article", "$.content[3].content[3].marks[0]"),
        ]);
        references.Media.ShouldBe(
        [
            new RichTextMediaReference(Guid.Parse("22222222-2222-2222-2222-222222222222"), "$.content[11]"),
        ]);
    }

    [Fact]
    public void FindsMediaInsideLinksAndNestedContent()
    {
        var references = RichTextReferenceExtractor.Extract(new RichText(Snapshot.Read("marks.json")));

        references.Media.ShouldBe(
        [
            new RichTextMediaReference(Guid.Parse("33333333-3333-3333-3333-333333333333"), "$.content[14].content[0]"),
        ]);
        references.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void KeepsEveryOccurrenceInDocumentOrder()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var media = Guid.NewGuid();
        var document = new DocumentNode(
        [
            new ParagraphNode(
            [
                new TextNode("a", [new EntryLinkMark(first, "article")]),
                new TextNode("b", [new EntryLinkMark(second, "author")]),
            ]),
            new BlockquoteNode([new ParagraphNode([new TextNode("c", [new EntryLinkMark(first, "article")])]), new MediaNode(media)]),
            new MediaNode(media),
        ]);

        var references = RichTextReferenceExtractor.Extract(document);

        references.Entries.Select(entry => (entry.Id, entry.Type, entry.Path)).ShouldBe(
        [
            (first, "article", "$.content[0].content[0].marks[0]"),
            (second, "author", "$.content[0].content[1].marks[0]"),
            (first, "article", "$.content[1].content[0].content[0].marks[0]"),
        ]);
        references.Media.Select(item => (item.Id, item.Path)).ShouldBe(
        [
            (media, "$.content[1].content[1]"),
            (media, "$.content[2]"),
        ]);
    }

    [Fact]
    public void ReturnsNothingWhenThereAreNoReferences()
    {
        var references = RichTextReferenceExtractor.Extract(RichText.Empty);

        references.Entries.ShouldBeEmpty();
        references.Media.ShouldBeEmpty();
    }

    [Fact]
    public void RefusesAnInvalidDocument()
    {
        var value = new RichText("""{"type":"doc","content":[{"type":"table"}]}""");

        Should.Throw<RichTextValidationException>(() => RichTextReferenceExtractor.Extract(value));
    }
}
