using System.Text;
using System.Text.RegularExpressions;
using FluentCMS.Schema;

namespace FluentCMS.Core.ProseMirror;

internal sealed partial class MarkdownRenderer(
    IReadOnlyDictionary<string, IRichTextNodeRenderer> nodeRenderers,
    RichTextRenderOptions options)
    : FormatRenderer(RichTextFormats.Markdown, nodeRenderers, options)
{
    public override string Render(DocumentNode document) => Blocks(document.Content);

    protected override string RenderChildren(RichTextNode node) =>
        node.Spec.Content switch
        {
            ContentKind.Blocks or ContentKind.ListItems => Blocks(node.Content),
            ContentKind.Inlines => Inlines(node.Content, singleLine: false),
            ContentKind.Text => ((CodeBlockNode)node).Code,
            _ => string.Empty,
        };

    private string Blocks(IReadOnlyList<RichTextNode> nodes)
    {
        var parts = new List<string>();
        RichTextNode? previous = null;
        var previousVariant = 0;

        foreach (var node in nodes)
        {
            // Two lists in a row would merge into one, so the second one uses the other marker.
            var variant = previous is not null && previous.GetType() == node.GetType() && node is BulletListNode or OrderedListNode
                ? 1 - previousVariant
                : 0;

            var text = Block(node, variant);
            if (text.Length > 0)
            {
                parts.Add(text);
            }

            previous = node;
            previousVariant = variant;
        }

        return string.Join("\n\n", parts);
    }

    private string Block(RichTextNode node, int variant)
    {
        if (RenderCustom(node, false) is { } custom)
        {
            return custom;
        }

        return node switch
        {
            ParagraphNode paragraph => Inlines(paragraph.Content, singleLine: false),
            HeadingNode heading => Heading(heading),
            BulletListNode list => List(list, variant == 0 ? "-" : "*", _ => string.Empty),
            OrderedListNode list => List(list, null, index => $"{list.Start + index}{(variant == 0 ? "." : ")")}"),
            BlockquoteNode quote => Quote(Blocks(quote.Content)),
            CodeBlockNode code => CodeBlock(code),
            HorizontalRuleNode => "---",
            MediaNode media => Media(media),
            InlineBlockNode block => Placeholder(block),
            _ => string.Empty,
        };
    }

    private string Heading(HeadingNode heading)
    {
        var text = Inlines(heading.Content, singleLine: true);
        var hashes = new string('#', heading.Level);
        return text.Length == 0 ? hashes : $"{hashes} {text}";
    }

    private string List(RichTextNode list, string? bullet, Func<int, string> ordered)
    {
        var items = list.Content.OfType<ListItemNode>().ToList();
        var tight = items.All(item => item.Content.Count(child => child is not (BulletListNode or OrderedListNode)) <= 1);
        var rendered = new List<string>();

        for (var index = 0; index < items.Count; index++)
        {
            var marker = bullet ?? ordered(index);
            var body = ItemBody(items[index], tight);
            rendered.Add(body.Length == 0 ? marker : Indent(body, marker + " ", new string(' ', marker.Length + 1)));
        }

        return string.Join(tight ? "\n" : "\n\n", rendered);
    }

    private string ItemBody(ListItemNode item, bool tight)
    {
        var builder = new StringBuilder();
        RichTextNode? previous = null;
        var bulletVariant = 0;
        var orderedVariant = 0;

        foreach (var child in item.Content)
        {
            var variant = child switch
            {
                BulletListNode when previous is BulletListNode => bulletVariant = 1 - bulletVariant,
                OrderedListNode when previous is OrderedListNode => orderedVariant = 1 - orderedVariant,
                _ => 0,
            };

            var text = Block(child, variant);
            if (text.Length == 0)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(tight && child is BulletListNode or OrderedListNode && previous is ParagraphNode ? "\n" : "\n\n");
            }

            builder.Append(text);
            previous = child;
        }

        return builder.ToString();
    }

    private static string Indent(string text, string first, string rest)
    {
        var lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            lines[index] = index == 0 ? first + lines[index] : lines[index].Length == 0 ? string.Empty : rest + lines[index];
        }

        return string.Join('\n', lines);
    }

    private static string Quote(string text) =>
        text.Length == 0
            ? ">"
            : string.Join('\n', text.Split('\n').Select(line => line.Length == 0 ? ">" : "> " + line));

    private static string CodeBlock(CodeBlockNode node)
    {
        var code = node.Code;
        var longest = 0;
        var run = 0;
        foreach (var character in code)
        {
            run = character == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }

        var fence = new string('`', Math.Max(3, longest + 1));
        var language = node.Language is { } value && LanguagePattern().IsMatch(value) ? value : string.Empty;
        return $"{fence}{language}\n{code}\n{fence}";
    }

    private string Media(MediaNode media)
    {
        var alt = (media.Alt ?? string.Empty).Replace('\n', ' ');
        alt = alt.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("[", "\\[", StringComparison.Ordinal).Replace("]", "\\]", StringComparison.Ordinal);
        var destination = ResolveMediaUrl(media)?.Trim() ?? $"fcms:media/{media.MediaId}";
        return $"![{alt}]({Destination(destination)}{Title(media.Title)})";
    }

    private static string Placeholder(InlineBlockNode block) => $"<!-- fcms-block: {block.BlockType} -->";

    private static string Destination(string url)
    {
        var builder = new StringBuilder(url.Length);
        foreach (var character in url)
        {
            builder.Append(character switch
            {
                '&' => "&amp;",
                ' ' => "%20",
                '(' => "%28",
                ')' => "%29",
                '<' => "%3C",
                '>' => "%3E",
                '\\' => "%5C",
                _ => character.ToString(),
            });
        }

        return builder.ToString();
    }

    private static string Title(string? title) =>
        string.IsNullOrEmpty(title)
            ? string.Empty
            : " \"" + title.Replace("&", "&amp;", StringComparison.Ordinal).Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace('\n', ' ') + "\"";

    private string Inlines(IReadOnlyList<RichTextNode> nodes, bool singleLine)
    {
        var builder = new StringBuilder();
        var open = new List<RichTextMark>();
        var pending = string.Empty;

        foreach (var node in nodes)
        {
            var marks = SortMarks(node.Marks).Where(mark => mark is not CodeMark).ToList();
            var hasCode = node.Marks.Any(mark => mark is CodeMark);
            var text = (node as TextNode)?.Text;

            if (text is not null && text.AsSpan().Trim().IsEmpty && marks.Count > 0)
            {
                marks = marks.GetRange(0, Math.Min(marks.Count, CommonPrefix(open, marks)));
            }

            var keep = CommonPrefix(open, marks);
            var closing = keep < open.Count;
            var opening = keep < marks.Count;

            var lead = string.Empty;
            var trail = string.Empty;
            if (text is not null && !hasCode)
            {
                var trimmedStart = text.TrimStart();
                var trimmed = trimmedStart.TrimEnd();
                if (trimmed.Length > 0)
                {
                    lead = opening ? text[..(text.Length - trimmedStart.Length)] : string.Empty;
                    trail = marks.Count > 0 ? trimmedStart[trimmed.Length..] : string.Empty;
                    text = text[lead.Length..(text.Length - trail.Length)];
                }
            }

            if (closing)
            {
                for (var index = open.Count - 1; index >= keep; index--)
                {
                    builder.Append(Close(open[index]));
                }

                open.RemoveRange(keep, open.Count - keep);
            }

            builder.Append(EscapeText(pending, singleLine, AtLineStart(builder)));
            pending = string.Empty;
            builder.Append(EscapeText(lead, singleLine, AtLineStart(builder)));

            for (var index = keep; index < marks.Count; index++)
            {
                builder.Append(Open(marks[index]));
                open.Add(marks[index]);
            }

            builder.Append(Inline(node, text, hasCode, singleLine, builder));
            pending = trail;
        }

        for (var index = open.Count - 1; index >= 0; index--)
        {
            builder.Append(Close(open[index]));
        }

        builder.Append(EscapeText(pending, singleLine, AtLineStart(builder)));
        return builder.ToString();
    }

    private static bool AtLineStart(StringBuilder builder) => builder.Length == 0 || builder[^1] == '\n';

    private string Inline(RichTextNode node, string? text, bool code, bool singleLine, StringBuilder before)
    {
        if (node is not TextNode && RenderCustom(node, true) is { } custom)
        {
            return custom;
        }

        switch (node)
        {
            case TextNode:
                return code ? CodeSpan(text!) : EscapeText(text!, singleLine, AtLineStart(before));
            case HardBreakNode:
                return singleLine ? " " : "\\\n";
            case MediaNode media:
                return Media(media);
            case InlineBlockNode block:
                return Placeholder(block);
            default:
                return string.Empty;
        }
    }

    private static string Open(RichTextMark mark) =>
        mark switch
        {
            BoldMark => "**",
            ItalicMark => "*",
            UnderlineMark => "<u>",
            StrikeMark => "~~",
            LinkMark link => SafeUrl.IsSafe(link.Href) ? "[" : string.Empty,
            EntryLinkMark => "[",
            _ => string.Empty,
        };

    private string Close(RichTextMark mark) =>
        mark switch
        {
            BoldMark => "**",
            ItalicMark => "*",
            UnderlineMark => "</u>",
            StrikeMark => "~~",
            LinkMark link => SafeUrl.IsSafe(link.Href) ? $"]({Destination(link.Href.Trim())}{Title(link.Title)})" : string.Empty,
            EntryLinkMark entry => $"]({Destination(ResolveEntryUrl(entry)?.Trim() ?? $"fcms:entry/{entry.EntryType}/{entry.EntryId}")})",
            _ => string.Empty,
        };

    private static string CodeSpan(string text)
    {
        var longest = 0;
        var run = 0;
        foreach (var character in text)
        {
            run = character == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }

        var fence = new string('`', longest == 0 ? 1 : longest + 1);
        var padded = text.StartsWith('`') || text.EndsWith('`') ? $" {text} " : text;
        return fence + padded.Replace('\n', ' ') + fence;
    }

    private static string EscapeText(string text, bool singleLine, bool atLineStart)
    {
        var builder = new StringBuilder(text.Length + 8);
        var nextLineStart = atLineStart && !singleLine;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            var lineStart = nextLineStart;
            nextLineStart = character == '\n' && !singleLine;

            switch (character)
            {
                case ' ' or '\t' when lineStart:
                    // Indentation is dropped, four spaces would start a code block, and the line stays at its start.
                    nextLineStart = true;
                    break;
                case '\\' or '`' or '*' or '_' or '[' or ']' or '<' or '~':
                    builder.Append('\\').Append(character);
                    break;
                case '>' or '#' when lineStart:
                    builder.Append('\\').Append(character);
                    break;
                case '+' or '-' when lineStart && IsMarkerLike(text, index):
                    builder.Append('\\').Append(character);
                    break;
                case '=' when lineStart && IsSetextUnderline(text, index):
                    builder.Append('\\').Append(character);
                    break;
                case '&' when StartsEntity(text, index):
                    builder.Append("\\&");
                    break;
                case >= '0' and <= '9' when lineStart:
                    var end = index;
                    while (end < text.Length && char.IsAsciiDigit(text[end]))
                    {
                        end++;
                    }

                    builder.Append(text, index, end - index);
                    if (end < text.Length && text[end] is '.' or ')' && (end + 1 == text.Length || text[end + 1] is ' ' or '\n'))
                    {
                        builder.Append('\\').Append(text[end]);
                        end++;
                    }

                    index = end - 1;
                    break;
                case '\n' when singleLine:
                    builder.Append(' ');
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

    private static bool IsMarkerLike(string text, int index) =>
        index + 1 == text.Length || text[index + 1] is ' ' or '\n' || (text[index] == '-' && text[index + 1] == '-');

    private static bool IsSetextUnderline(string text, int index)
    {
        for (var position = index; position < text.Length && text[position] != '\n'; position++)
        {
            if (text[position] is not ('=' or ' ' or '\t'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool StartsEntity(string text, int index)
    {
        var end = index + 1;
        if (end < text.Length && text[end] == '#')
        {
            end++;
        }

        var start = end;
        while (end < text.Length && char.IsAsciiLetterOrDigit(text[end]))
        {
            end++;
        }

        return end > start && end < text.Length && text[end] == ';';
    }

    [GeneratedRegex("^[A-Za-z0-9_+#.-]+$")]
    private static partial Regex LanguagePattern();
}
