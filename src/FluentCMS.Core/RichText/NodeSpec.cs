namespace FluentCMS.Core.ProseMirror;

internal enum NodePosition
{
    Root,
    Block,
    Inline,
    BlockOrInline,
    ListItem,
}

internal enum ContentKind
{
    None,
    Blocks,
    Inlines,
    ListItems,
    Text,
}

internal sealed record NodeSpec(string Type, NodePosition Position, ContentKind Content, bool AllowsMarks)
{
    public static readonly NodeSpec Doc = new(RichTextNodeTypes.Doc, NodePosition.Root, ContentKind.Blocks, false);
    public static readonly NodeSpec Paragraph = new(RichTextNodeTypes.Paragraph, NodePosition.Block, ContentKind.Inlines, false);
    public static readonly NodeSpec Heading = new(RichTextNodeTypes.Heading, NodePosition.Block, ContentKind.Inlines, false);
    public static readonly NodeSpec BulletList = new(RichTextNodeTypes.BulletList, NodePosition.Block, ContentKind.ListItems, false);
    public static readonly NodeSpec OrderedList = new(RichTextNodeTypes.OrderedList, NodePosition.Block, ContentKind.ListItems, false);
    public static readonly NodeSpec ListItem = new(RichTextNodeTypes.ListItem, NodePosition.ListItem, ContentKind.Blocks, false);
    public static readonly NodeSpec Blockquote = new(RichTextNodeTypes.Blockquote, NodePosition.Block, ContentKind.Blocks, false);
    public static readonly NodeSpec CodeBlock = new(RichTextNodeTypes.CodeBlock, NodePosition.Block, ContentKind.Text, false);
    public static readonly NodeSpec HorizontalRule = new(RichTextNodeTypes.HorizontalRule, NodePosition.Block, ContentKind.None, false);
    public static readonly NodeSpec HardBreak = new(RichTextNodeTypes.HardBreak, NodePosition.Inline, ContentKind.None, true);
    public static readonly NodeSpec Text = new(RichTextNodeTypes.Text, NodePosition.Inline, ContentKind.None, true);
    public static readonly NodeSpec Media = new(RichTextNodeTypes.Media, NodePosition.BlockOrInline, ContentKind.None, true);
    public static readonly NodeSpec InlineBlock = new(RichTextNodeTypes.InlineBlock, NodePosition.BlockOrInline, ContentKind.None, true);

    private static readonly Dictionary<string, NodeSpec> byType = new[]
    {
        Doc, Paragraph, Heading, BulletList, OrderedList, ListItem, Blockquote, CodeBlock,
        HorizontalRule, HardBreak, Text, Media, InlineBlock,
    }.ToDictionary(spec => spec.Type, StringComparer.Ordinal);

    public static bool TryGet(string type, out NodeSpec spec) => byType.TryGetValue(type, out spec!);

    public static bool IsReserved(string name) => byType.ContainsKey(name);
}
