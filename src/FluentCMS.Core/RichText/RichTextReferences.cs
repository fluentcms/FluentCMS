using System.Text.Json;
using FluentCMS.Schema;

namespace FluentCMS.Core.ProseMirror;

public sealed record RichTextEntryReference(Guid Id, string Type, string Path);

public sealed record RichTextMediaReference(Guid Id, string Path);

public sealed record RichTextInlineBlock(string BlockType, IReadOnlyDictionary<string, JsonElement> Data, string Path);

public sealed record RichTextReferences(
    IReadOnlyList<RichTextEntryReference> Entries,
    IReadOnlyList<RichTextMediaReference> Media,
    IReadOnlyList<RichTextInlineBlock> InlineBlocks);

public static class RichTextReferenceExtractor
{
    public static RichTextReferences Extract(RichText value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Extract(RichTextParser.Parse(value));
    }

    public static RichTextReferences Extract(DocumentNode document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var entries = new List<RichTextEntryReference>();
        var media = new List<RichTextMediaReference>();
        var blocks = new List<RichTextInlineBlock>();
        Visit(document, "$", entries, media, blocks);
        return new RichTextReferences(entries, media, blocks);
    }

    private static void Visit(RichTextNode node, string path, List<RichTextEntryReference> entries, List<RichTextMediaReference> media, List<RichTextInlineBlock> blocks)
    {
        if (node is InlineBlockNode block)
        {
            blocks.Add(new RichTextInlineBlock(block.BlockType, block.Data, path));
        }

        if (node is MediaNode mediaNode)
        {
            media.Add(new RichTextMediaReference(mediaNode.MediaId, path));
        }

        for (var index = 0; index < node.Marks.Count; index++)
        {
            if (node.Marks[index] is EntryLinkMark link)
            {
                entries.Add(new RichTextEntryReference(link.EntryId, link.EntryType, $"{path}.marks[{index}]"));
            }
        }

        for (var index = 0; index < node.Content.Count; index++)
        {
            Visit(node.Content[index], $"{path}.content[{index}]", entries, media, blocks);
        }
    }
}
