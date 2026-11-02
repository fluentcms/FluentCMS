using System.Text.Json;
using FluentCMS.Schema;

namespace FluentCMS.Core.ProseMirror;

internal abstract class FormatRenderer(
    string format,
    IReadOnlyDictionary<string, IRichTextNodeRenderer> nodeRenderers,
    RichTextRenderOptions options)
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

    protected string? ResolveEntryUrl(EntryLinkMark mark) =>
        SafeUrl.OrNull(options.EntryUrlResolver?.Invoke(mark.EntryType, mark.EntryId));

    protected string? ResolveMediaUrl(MediaNode media) =>
        SafeUrl.OrNull(options.MediaUrlResolver?.Invoke(media.MediaId));

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

internal static class SafeUrl
{
    private static readonly string[] allowedSchemes = ["http", "https", "mailto", "tel"];

    public static string? OrNull(string? url) => url is not null && IsSafe(url) ? url : null;

    public static bool IsSafe(string url)
    {
        var trimmed = url.Trim();
        if (trimmed.Any(char.IsControl))
        {
            return false;
        }

        var end = trimmed.IndexOfAny(['/', '?', '#', ':']);
        if (end < 0 || trimmed[end] != ':')
        {
            return true;
        }

        return allowedSchemes.Contains(trimmed[..end], StringComparer.OrdinalIgnoreCase);
    }
}
