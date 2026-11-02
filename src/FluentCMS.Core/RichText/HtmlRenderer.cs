using System.Text;
using System.Text.RegularExpressions;
using FluentCMS.Schema;

namespace FluentCMS.Core.ProseMirror;

internal sealed partial class HtmlRenderer(
    IReadOnlyDictionary<string, IRichTextNodeRenderer> nodeRenderers,
    RichTextRenderOptions options)
    : FormatRenderer(RichTextFormats.Html, nodeRenderers, options)
{
    public override string Render(DocumentNode document) => Blocks(document.Content);

    protected override string RenderChildren(RichTextNode node) =>
        node.Spec.Content switch
        {
            ContentKind.Blocks or ContentKind.ListItems => Blocks(node.Content),
            ContentKind.Inlines => Inlines(node.Content),
            ContentKind.Text => Escape(((CodeBlockNode)node).Code),
            _ => string.Empty,
        };

    private string Blocks(IReadOnlyList<RichTextNode> nodes) => string.Join('\n', nodes.Select(Block));

    private string Block(RichTextNode node)
    {
        if (RenderCustom(node, false) is { } custom)
        {
            return custom;
        }

        return node switch
        {
            ParagraphNode paragraph => $"<p>{Inlines(paragraph.Content)}</p>",
            HeadingNode heading => $"<h{heading.Level}>{Inlines(heading.Content)}</h{heading.Level}>",
            BulletListNode list => Wrap("<ul>", "</ul>", Blocks(list.Content)),
            OrderedListNode list => Wrap(list.Start == 1 ? "<ol>" : $"<ol start=\"{list.Start}\">", "</ol>", Blocks(list.Content)),
            ListItemNode item => $"<li>{Blocks(item.Content)}</li>",
            BlockquoteNode quote => Wrap("<blockquote>", "</blockquote>", Blocks(quote.Content)),
            CodeBlockNode code => CodeBlock(code),
            HorizontalRuleNode => "<hr>",
            MediaNode media => $"<figure>{Media(media)}</figure>",
            InlineBlockNode block => PlaceholderBlock(block, "div"),
            _ => string.Empty,
        };
    }

    private static string Wrap(string open, string close, string inner) =>
        inner.Length == 0 ? open + close : $"{open}\n{inner}\n{close}";

    private static string CodeBlock(CodeBlockNode code)
    {
        var language = code.Language;
        var attribute = language is not null && LanguagePattern().IsMatch(language)
            ? $" class=\"language-{language}\""
            : string.Empty;

        return $"<pre><code{attribute}>{Escape(code.Code)}</code></pre>";
    }

    private string Inlines(IReadOnlyList<RichTextNode> nodes)
    {
        var builder = new StringBuilder();
        var open = new List<RichTextMark>();

        foreach (var node in nodes)
        {
            var marks = SortMarks(node.Marks);
            var keep = CommonPrefix(open, marks);

            for (var index = open.Count - 1; index >= keep; index--)
            {
                builder.Append(Close(open[index]));
            }

            open.RemoveRange(keep, open.Count - keep);

            for (var index = keep; index < marks.Count; index++)
            {
                builder.Append(Open(marks[index]));
                open.Add(marks[index]);
            }

            builder.Append(Inline(node));
        }

        for (var index = open.Count - 1; index >= 0; index--)
        {
            builder.Append(Close(open[index]));
        }

        return builder.ToString();
    }

    private string Inline(RichTextNode node)
    {
        if (node is not TextNode && RenderCustom(node, true) is { } custom)
        {
            return custom;
        }

        return node switch
        {
            TextNode text => Escape(text.Text),
            HardBreakNode => "<br>",
            MediaNode media => Media(media),
            InlineBlockNode block => PlaceholderBlock(block, "span"),
            _ => string.Empty,
        };
    }

    private string Open(RichTextMark mark) =>
        mark switch
        {
            BoldMark => "<strong>",
            ItalicMark => "<em>",
            UnderlineMark => "<u>",
            StrikeMark => "<s>",
            CodeMark => "<code>",
            LinkMark link => OpenLink(link),
            EntryLinkMark entry => OpenEntryLink(entry),
            _ => string.Empty,
        };

    private static string Close(RichTextMark mark) =>
        mark switch
        {
            BoldMark => "</strong>",
            ItalicMark => "</em>",
            UnderlineMark => "</u>",
            StrikeMark => "</s>",
            CodeMark => "</code>",
            LinkMark link => SafeUrl.IsSafe(link.Href) ? "</a>" : string.Empty,
            EntryLinkMark => "</a>",
            _ => string.Empty,
        };

    private static string OpenLink(LinkMark link)
    {
        if (!SafeUrl.IsSafe(link.Href))
        {
            return string.Empty;
        }

        var tag = new StringBuilder("<a href=\"").Append(EscapeAttribute(link.Href.Trim())).Append('"');
        if (!string.IsNullOrEmpty(link.Title))
        {
            tag.Append(" title=\"").Append(EscapeAttribute(link.Title)).Append('"');
        }

        if (link.Target == "_blank")
        {
            tag.Append(" target=\"_blank\" rel=\"noopener noreferrer\"");
        }

        return tag.Append('>').ToString();
    }

    private string OpenEntryLink(EntryLinkMark entry)
    {
        var tag = new StringBuilder("<a");
        if (ResolveEntryUrl(entry) is { } url)
        {
            tag.Append(" href=\"").Append(EscapeAttribute(url.Trim())).Append('"');
        }

        return tag
            .Append(" data-entry-type=\"").Append(EscapeAttribute(entry.EntryType))
            .Append("\" data-entry-id=\"").Append(entry.EntryId).Append("\">")
            .ToString();
    }

    private string Media(MediaNode media)
    {
        var tag = new StringBuilder("<img");
        if (ResolveMediaUrl(media) is { } url)
        {
            tag.Append(" src=\"").Append(EscapeAttribute(url.Trim())).Append('"');
        }

        tag.Append(" alt=\"").Append(EscapeAttribute(media.Alt ?? string.Empty)).Append('"');
        if (!string.IsNullOrEmpty(media.Title))
        {
            tag.Append(" title=\"").Append(EscapeAttribute(media.Title)).Append('"');
        }

        return tag.Append(" data-media-id=\"").Append(media.MediaId).Append("\">").ToString();
    }

    private static string PlaceholderBlock(InlineBlockNode block, string tag)
    {
        var data = block.Attributes.TryGetValue("data", out var value) && value.ValueKind == System.Text.Json.JsonValueKind.Object
            ? $" data-fcms-data=\"{EscapeAttribute(Compact(value))}\""
            : string.Empty;

        return $"<{tag} data-fcms-block=\"{EscapeAttribute(block.BlockType)}\"{data}></{tag}>";
    }

    private static string Escape(string text) => Encode(text, attribute: false);

    private static string EscapeAttribute(string text) => Encode(text, attribute: true);

    private static string Encode(string text, bool attribute)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            switch (character)
            {
                case '&':
                    builder.Append("&amp;");
                    break;
                case '<':
                    builder.Append("&lt;");
                    break;
                case '>':
                    builder.Append("&gt;");
                    break;
                case '"' when attribute:
                    builder.Append("&quot;");
                    break;
                case '\'' when attribute:
                    builder.Append("&#39;");
                    break;
                case '\0':
                    builder.Append('�');
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        return builder.ToString();
    }

    [GeneratedRegex("^[A-Za-z0-9_+#.-]+$")]
    private static partial Regex LanguagePattern();
}
