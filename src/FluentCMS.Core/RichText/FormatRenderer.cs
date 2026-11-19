using System.Text.Json;
using FluentCMS.Schema;

namespace FluentCMS.Core.ProseMirror;

internal abstract class FormatRenderer(
    string format,
    IReadOnlyDictionary<string, IRichTextNodeRenderer> nodeRenderers,
    RichTextRenderOptions options,
    SafeUrl links)
{
    public abstract string Render(DocumentNode document);

    protected abstract string RenderChildren(RichTextNode node);

    protected string? RenderCustom(RichTextNode node, bool inline)
    {
        var key = node is InlineBlockNode block ? block.BlockType : node.Type;
        if (!nodeRenderers.TryGetValue(key, out var renderer))
        {
            return null;
        }

        return renderer.Render(new NodeContext(format, key, node is InlineBlockNode b ? b.Data : node.Attributes, inline, () => RenderChildren(node)));
    }

    protected SafeUrl Links => links;

    protected string? ResolveEntryUrl(EntryLinkMark mark) =>
        links.OrNull(options.EntryUrlResolver?.Invoke(mark.EntryType, mark.EntryId));

    protected string? ResolveMediaUrl(MediaNode media) =>
        links.OrNull(options.MediaUrlResolver?.Invoke(media.MediaId));

    // The node's own text wins; the asset's is for a node that has none.
    protected string MediaAlt(MediaNode media) =>
        string.IsNullOrEmpty(media.Alt) ? options.MediaAltResolver?.Invoke(media.MediaId) ?? string.Empty : media.Alt;

    protected static List<RichTextMark> SortMarks(IReadOnlyList<RichTextMark> marks) =>
        [.. marks.OrderBy(Rank)];

    protected static int CommonPrefix(List<RichTextMark> open, List<RichTextMark> marks)
    {
        var keep = 0;
        while (keep < open.Count && keep < marks.Count && open[keep].SameAs(marks[keep]))
        {
            keep++;
        }

        return keep;
    }

    protected static string Compact(JsonElement element) => RichTextSerializer.Compact(element);

    private static int Rank(RichTextMark mark) =>
        mark switch
        {
            LinkMark => 0,
            EntryLinkMark => 1,
            BoldMark => 2,
            ItalicMark => 3,
            UnderlineMark => 4,
            StrikeMark => 5,
            _ => 6,
        };

    private sealed class NodeContext(
        string format,
        string nodeType,
        IReadOnlyDictionary<string, JsonElement> attributes,
        bool isInline,
        Func<string> renderChildren) : IRichTextNodeContext
    {
        public string Format { get; } = format;

        public string NodeType { get; } = nodeType;

        public IReadOnlyDictionary<string, JsonElement> Attributes { get; } = attributes;

        public bool IsInline { get; } = isInline;

        public string RenderChildren() => renderChildren();
    }
}
