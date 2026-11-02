using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace FluentCMS.Core.ProseMirror;

public abstract class RichTextNode
{
    private readonly ImmutableArray<RichTextNode> content;
    private readonly ImmutableArray<RichTextMark> marks;

    private protected RichTextNode(
        NodeSpec spec,
        ImmutableSortedDictionary<string, JsonElement>? attributes,
        IEnumerable<RichTextNode>? content,
        IEnumerable<RichTextMark>? marks)
    {
        Spec = spec;
        Attributes = attributes ?? RichTextAttributes.Empty;
        this.content = content is null ? [] : [.. content];
        this.marks = marks is null ? [] : [.. marks];
    }

    internal NodeSpec Spec { get; }

    public string Type => Spec.Type;

    public IReadOnlyDictionary<string, JsonElement> Attributes { get; }

    public IReadOnlyList<RichTextNode> Content => content;

    public IReadOnlyList<RichTextMark> Marks => marks;

    private protected string? OptionalString(string name) =>
        Attributes.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

public sealed class DocumentNode : RichTextNode
{
    public DocumentNode(IEnumerable<RichTextNode>? content = null)
        : base(NodeSpec.Doc, null, content, null)
    {
    }

    internal DocumentNode(ImmutableSortedDictionary<string, JsonElement> attributes, IEnumerable<RichTextNode>? content)
        : base(NodeSpec.Doc, attributes, content, null)
    {
    }
}

public sealed class ParagraphNode : RichTextNode
{
    public ParagraphNode(IEnumerable<RichTextNode>? content = null)
        : base(NodeSpec.Paragraph, null, content, null)
    {
    }

    internal ParagraphNode(ImmutableSortedDictionary<string, JsonElement> attributes, IEnumerable<RichTextNode>? content)
        : base(NodeSpec.Paragraph, attributes, content, null)
    {
    }
}

public sealed class HeadingNode : RichTextNode
{
    public HeadingNode(int level, IEnumerable<RichTextNode>? content = null)
        : base(NodeSpec.Heading, RichTextAttributes.Create(("level", RichTextAttributes.Number(Checked(level)))), content, null)
    {
    }

    internal HeadingNode(ImmutableSortedDictionary<string, JsonElement> attributes, IEnumerable<RichTextNode>? content)
        : base(NodeSpec.Heading, attributes, content, null)
    {
    }

    public int Level => Attributes["level"].GetInt32();

    private static int Checked(int level) =>
        RichTextAttributes.CheckHeadingLevel(level) is { } error
            ? throw new ArgumentOutOfRangeException(nameof(level), level, error)
            : level;
}

public sealed class BulletListNode : RichTextNode
{
    public BulletListNode(IEnumerable<ListItemNode>? items = null)
        : base(NodeSpec.BulletList, null, items, null)
    {
    }

    internal BulletListNode(ImmutableSortedDictionary<string, JsonElement> attributes, IEnumerable<RichTextNode>? content)
        : base(NodeSpec.BulletList, attributes, content, null)
    {
    }
}

public sealed class OrderedListNode : RichTextNode
{
    public OrderedListNode(IEnumerable<ListItemNode>? items = null, int? start = null)
        : base(
            NodeSpec.OrderedList,
            start is null ? null : RichTextAttributes.Create(("start", RichTextAttributes.Number(CheckedStart(start.Value)))),
            items,
            null)
    {
    }

    internal OrderedListNode(ImmutableSortedDictionary<string, JsonElement> attributes, IEnumerable<RichTextNode>? content)
        : base(NodeSpec.OrderedList, attributes, content, null)
    {
    }

    public int Start => Attributes.TryGetValue("start", out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 1;

    private static int CheckedStart(int start) =>
        RichTextAttributes.CheckListStart(start) is { } error
            ? throw new ArgumentOutOfRangeException(nameof(start), start, error)
            : start;
}

public sealed class ListItemNode : RichTextNode
{
    public ListItemNode(IEnumerable<RichTextNode>? content = null)
        : base(NodeSpec.ListItem, null, content, null)
    {
    }

    internal ListItemNode(ImmutableSortedDictionary<string, JsonElement> attributes, IEnumerable<RichTextNode>? content)
        : base(NodeSpec.ListItem, attributes, content, null)
    {
    }
}

public sealed class BlockquoteNode : RichTextNode
{
    public BlockquoteNode(IEnumerable<RichTextNode>? content = null)
        : base(NodeSpec.Blockquote, null, content, null)
    {
    }

    internal BlockquoteNode(ImmutableSortedDictionary<string, JsonElement> attributes, IEnumerable<RichTextNode>? content)
        : base(NodeSpec.Blockquote, attributes, content, null)
    {
    }
}

public sealed class CodeBlockNode : RichTextNode
{
    public CodeBlockNode(string? code = null, string? language = null)
        : base(
            NodeSpec.CodeBlock,
            language is null ? null : RichTextAttributes.Create(("language", RichTextAttributes.String(language))),
            string.IsNullOrEmpty(code) ? null : [new TextNode(code)],
            null)
    {
    }

    internal CodeBlockNode(ImmutableSortedDictionary<string, JsonElement> attributes, IEnumerable<RichTextNode>? content)
        : base(NodeSpec.CodeBlock, attributes, content, null)
    {
    }

    public string? Language => OptionalString("language");

    public string Code
    {
        get
        {
            var builder = new StringBuilder();
            foreach (var child in Content.OfType<TextNode>())
            {
                builder.Append(child.Text);
            }

            return builder.ToString();
        }
    }
}

public sealed class HorizontalRuleNode : RichTextNode
{
    public HorizontalRuleNode()
        : base(NodeSpec.HorizontalRule, null, null, null)
    {
    }

    internal HorizontalRuleNode(ImmutableSortedDictionary<string, JsonElement> attributes)
        : base(NodeSpec.HorizontalRule, attributes, null, null)
    {
    }
}

public sealed class HardBreakNode : RichTextNode
{
    public HardBreakNode(IEnumerable<RichTextMark>? marks = null)
        : base(NodeSpec.HardBreak, null, null, marks)
    {
    }

    internal HardBreakNode(ImmutableSortedDictionary<string, JsonElement> attributes, IEnumerable<RichTextMark>? marks)
        : base(NodeSpec.HardBreak, attributes, null, marks)
    {
    }
}

public sealed class TextNode : RichTextNode
{
    public TextNode(string text, IEnumerable<RichTextMark>? marks = null)
        : base(NodeSpec.Text, null, null, marks)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        Text = text;
    }

    internal TextNode(ImmutableSortedDictionary<string, JsonElement> attributes, string text, IEnumerable<RichTextMark>? marks)
        : base(NodeSpec.Text, attributes, null, marks)
    {
        Text = text;
    }

    public string Text { get; }
}

public sealed class MediaNode : RichTextNode
{
    public MediaNode(Guid mediaId, string? alt = null, string? title = null, IEnumerable<RichTextMark>? marks = null)
        : base(NodeSpec.Media, BuildAttributes(mediaId, alt, title), null, marks)
    {
    }

    internal MediaNode(ImmutableSortedDictionary<string, JsonElement> attributes, IEnumerable<RichTextMark>? marks)
        : base(NodeSpec.Media, attributes, null, marks)
    {
    }

    public Guid MediaId => Guid.Parse(Attributes["mediaId"].GetString()!);

    public string? Alt => OptionalString("alt");

    public string? Title => OptionalString("title");

    private static ImmutableSortedDictionary<string, JsonElement> BuildAttributes(Guid mediaId, string? alt, string? title)
    {
        var attributes = new List<(string Name, JsonElement Value)> { ("mediaId", RichTextAttributes.String(mediaId.ToString())) };
        if (alt is not null)
        {
            attributes.Add(("alt", RichTextAttributes.String(alt)));
        }

        if (title is not null)
        {
            attributes.Add(("title", RichTextAttributes.String(title)));
        }

        return RichTextAttributes.Create([.. attributes]);
    }
}

public sealed class InlineBlockNode : RichTextNode
{
    public InlineBlockNode(string blockType, IReadOnlyDictionary<string, JsonElement>? data = null, IEnumerable<RichTextMark>? marks = null)
        : base(NodeSpec.InlineBlock, BuildAttributes(blockType, data), null, marks)
    {
    }

    internal InlineBlockNode(ImmutableSortedDictionary<string, JsonElement> attributes, IEnumerable<RichTextMark>? marks)
        : base(NodeSpec.InlineBlock, attributes, null, marks)
    {
    }

    public string BlockType => Attributes["blockType"].GetString()!;

    public IReadOnlyDictionary<string, JsonElement> Data =>
        Attributes.TryGetValue("data", out var data) && data.ValueKind == JsonValueKind.Object
            ? data.EnumerateObject().ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal)
            : ImmutableDictionary<string, JsonElement>.Empty;

    private static ImmutableSortedDictionary<string, JsonElement> BuildAttributes(string blockType, IReadOnlyDictionary<string, JsonElement>? data)
    {
        if (RichTextAttributes.CheckBlockType(blockType) is { } error)
        {
            throw new ArgumentException(error, nameof(blockType));
        }

        var attributes = new List<(string Name, JsonElement Value)> { ("blockType", RichTextAttributes.String(blockType)) };
        if (data is not null)
        {
            attributes.Add(("data", JsonSerializer.SerializeToElement(data)));
        }

        return RichTextAttributes.Create([.. attributes]);
    }
}
