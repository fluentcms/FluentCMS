using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using FluentCMS.Core.Content;
using FluentCMS.Schema;

namespace FluentCMS.Core.ProseMirror;

public static class RichTextParser
{
    private static readonly JsonDocumentOptions options = new() { MaxDepth = JsonDepth.Max, AllowDuplicateProperties = false };

    public static DocumentNode Parse(RichText value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Parse(value.Json);
    }

    public static DocumentNode Parse(string json) =>
        TryParse(json, out var document, out var errors) ? document : throw new RichTextValidationException(errors);

    public static bool TryParse(RichText value, [NotNullWhen(true)] out DocumentNode? document, out IReadOnlyList<RichTextValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(value);
        return TryParse(value.Json, out document, out errors);
    }

    public static bool TryParse(string json, [NotNullWhen(true)] out DocumentNode? document, out IReadOnlyList<RichTextValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(json);

        var found = new List<RichTextValidationError>();
        document = null;

        try
        {
            using var parsed = JsonDocument.Parse(json, options);
            var node = new Reader(found).ReadNode(parsed.RootElement, "$");

            if (node is DocumentNode root)
            {
                found.AddRange(RichTextValidator.Validate(root));
                if (found.Count == 0)
                {
                    document = root;
                }
            }
            else if (node is not null)
            {
                found.Add(new RichTextValidationError("$", $"The root node must be of type '{RichTextNodeTypes.Doc}', not '{node.Type}'."));
            }
        }
        catch (JsonException exception)
        {
            found.Add(new RichTextValidationError("$", $"The document is not valid JSON: {exception.Message}"));
        }

        errors = found;
        return document is not null;
    }

    private sealed class Reader(List<RichTextValidationError> errors)
    {
        public RichTextNode? ReadNode(JsonElement element, string path)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                Add(path, "Expected an object.");
                return null;
            }

            var before = errors.Count;
            string? type = null;
            JsonElement? attrs = null, content = null, marks = null, text = null;

            foreach (var property in element.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "type":
                        type = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                        if (type is null)
                        {
                            Add($"{path}.type", "The type must be a string.");
                        }

                        break;
                    case "attrs":
                        attrs = property.Value;
                        break;
                    case "content":
                        content = property.Value;
                        break;
                    case "marks":
                        marks = property.Value;
                        break;
                    case "text":
                        text = property.Value;
                        break;
                    default:
                        Add(path, $"Unknown property '{property.Name}'.");
                        break;
                }
            }

            if (type is null)
            {
                if (errors.Count == before)
                {
                    Add(path, "The node has no type.");
                }

                return null;
            }

            if (!NodeSpec.TryGet(type, out var spec))
            {
                Add(path, $"Unknown node type '{type}'.");
                return null;
            }

            var attributes = attrs is null ? RichTextAttributes.Empty : ReadAttributes(attrs.Value, $"{path}.attrs");
            if (attributes is not null)
            {
                CheckNodeAttributes(spec, attributes, $"{path}.attrs");
            }

            var children = ReadChildren(spec, content, path);
            var nodeMarks = ReadMarks(spec, marks, path);
            var textValue = ReadText(spec, text, path);

            if (errors.Count > before || attributes is null || children is null || nodeMarks is null)
            {
                return null;
            }

            return Create(spec, attributes, children, nodeMarks, textValue);
        }

        private List<RichTextNode>? ReadChildren(NodeSpec spec, JsonElement? content, string path)
        {
            var children = new List<RichTextNode>();
            if (content is null)
            {
                return children;
            }

            if (content.Value.ValueKind != JsonValueKind.Array)
            {
                Add($"{path}.content", "The content must be an array.");
                return null;
            }

            if (content.Value.GetArrayLength() > 0 && spec.Content == ContentKind.None)
            {
                Add($"{path}.content", $"A '{spec.Type}' node cannot have content.");
                return null;
            }

            var ok = true;
            var index = 0;
            foreach (var item in content.Value.EnumerateArray())
            {
                var child = ReadNode(item, $"{path}.content[{index++}]");
                if (child is null)
                {
                    ok = false;
                }
                else
                {
                    children.Add(child);
                }
            }

            return ok ? children : null;
        }

        private List<RichTextMark>? ReadMarks(NodeSpec spec, JsonElement? marks, string path)
        {
            var result = new List<RichTextMark>();
            if (marks is null)
            {
                return result;
            }

            if (marks.Value.ValueKind != JsonValueKind.Array)
            {
                Add($"{path}.marks", "The marks must be an array.");
                return null;
            }

            if (marks.Value.GetArrayLength() > 0 && !spec.AllowsMarks)
            {
                Add($"{path}.marks", $"A '{spec.Type}' node cannot have marks.");
                return null;
            }

            var ok = true;
            var index = 0;
            foreach (var item in marks.Value.EnumerateArray())
            {
                var mark = ReadMark(item, $"{path}.marks[{index++}]");
                if (mark is null)
                {
                    ok = false;
                }
                else
                {
                    result.Add(mark);
                }
            }

            return ok ? result : null;
        }

        private string? ReadText(NodeSpec spec, JsonElement? text, string path)
        {
            if (spec != NodeSpec.Text)
            {
                if (text is not null)
                {
                    Add($"{path}.text", $"A '{spec.Type}' node cannot have text.");
                }

                return null;
            }

            if (text is null || text.Value.ValueKind != JsonValueKind.String || text.Value.GetString() is not { Length: > 0 } value)
            {
                Add($"{path}.text", "A text node needs a non-empty string.");
                return null;
            }

            return value;
        }

        private RichTextMark? ReadMark(JsonElement element, string path)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                Add(path, "Expected an object.");
                return null;
            }

            var before = errors.Count;
            string? type = null;
            JsonElement? attrs = null;

            foreach (var property in element.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "type":
                        type = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                        if (type is null)
                        {
                            Add($"{path}.type", "The type must be a string.");
                        }

                        break;
                    case "attrs":
                        attrs = property.Value;
                        break;
                    default:
                        Add(path, $"Unknown property '{property.Name}'.");
                        break;
                }
            }

            if (type is null)
            {
                if (errors.Count == before)
                {
                    Add(path, "The mark has no type.");
                }

                return null;
            }

            if (type is not (RichTextMarkTypes.Bold or RichTextMarkTypes.Italic or RichTextMarkTypes.Underline
                or RichTextMarkTypes.Strike or RichTextMarkTypes.Code or RichTextMarkTypes.Link or RichTextMarkTypes.EntryLink))
            {
                Add(path, $"Unknown mark type '{type}'.");
                return null;
            }

            var attributes = attrs is null ? RichTextAttributes.Empty : ReadAttributes(attrs.Value, $"{path}.attrs");
            if (attributes is not null)
            {
                CheckMarkAttributes(type, attributes, $"{path}.attrs");
            }

            if (errors.Count > before || attributes is null)
            {
                return null;
            }

            return type switch
            {
                RichTextMarkTypes.Bold => new BoldMark(attributes),
                RichTextMarkTypes.Italic => new ItalicMark(attributes),
                RichTextMarkTypes.Underline => new UnderlineMark(attributes),
                RichTextMarkTypes.Strike => new StrikeMark(attributes),
                RichTextMarkTypes.Code => new CodeMark(attributes),
                RichTextMarkTypes.Link => new LinkMark(attributes),
                _ => new EntryLinkMark(attributes),
            };
        }

        private ImmutableSortedDictionary<string, JsonElement>? ReadAttributes(JsonElement element, string path)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                Add(path, "The attributes must be an object.");
                return null;
            }

            return RichTextAttributes.Create(element.EnumerateObject().Select(property =>
                KeyValuePair.Create(property.Name, property.Value.Clone())));
        }

        private void CheckNodeAttributes(NodeSpec spec, ImmutableSortedDictionary<string, JsonElement> attributes, string path)
        {
            switch (spec.Type)
            {
                case RichTextNodeTypes.Heading:
                    if (!attributes.TryGetValue("level", out var level)
                        || level.ValueKind != JsonValueKind.Number
                        || !level.TryGetInt32(out var levelValue)
                        || RichTextAttributes.CheckHeadingLevel(levelValue) is not null)
                    {
                        Add($"{path}.level", "The heading level must be an integer between 1 and 6.");
                    }

                    break;
                case RichTextNodeTypes.OrderedList:
                    if (attributes.TryGetValue("start", out var start) && start.ValueKind != JsonValueKind.Null)
                    {
                        if (start.ValueKind != JsonValueKind.Number || !start.TryGetInt32(out var startValue))
                        {
                            Add($"{path}.start", "The start must be an integer.");
                        }
                        else if (RichTextAttributes.CheckListStart(startValue) is { } startError)
                        {
                            Add($"{path}.start", startError);
                        }
                    }

                    break;
                case RichTextNodeTypes.CodeBlock:
                    CheckOptionalString(attributes, "language", path);
                    break;
                case RichTextNodeTypes.Media:
                    CheckRequiredGuid(attributes, "mediaId", path);
                    CheckOptionalString(attributes, "alt", path);
                    CheckOptionalString(attributes, "title", path);
                    break;
                case RichTextNodeTypes.InlineBlock:
                    if (!attributes.TryGetValue("blockType", out var blockType) || blockType.ValueKind != JsonValueKind.String)
                    {
                        Add($"{path}.blockType", "The block type must be a string.");
                    }
                    else if (RichTextAttributes.CheckBlockType(blockType.GetString()) is { } error)
                    {
                        Add($"{path}.blockType", error);
                    }

                    if (attributes.TryGetValue("data", out var data) && data.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null))
                    {
                        Add($"{path}.data", "The block data must be an object.");
                    }

                    break;
            }
        }

        private void CheckMarkAttributes(string type, ImmutableSortedDictionary<string, JsonElement> attributes, string path)
        {
            switch (type)
            {
                case RichTextMarkTypes.Link:
                    if (!attributes.TryGetValue("href", out var href) || href.ValueKind != JsonValueKind.String)
                    {
                        Add($"{path}.href", "The href must be a string.");
                    }

                    CheckOptionalString(attributes, "title", path);
                    CheckOptionalString(attributes, "target", path);
                    break;
                case RichTextMarkTypes.EntryLink:
                    CheckRequiredGuid(attributes, "entryId", path);
                    if (!attributes.TryGetValue("entryType", out var entryType) || entryType.ValueKind != JsonValueKind.String)
                    {
                        Add($"{path}.entryType", "The entry type must be a string.");
                    }
                    else if (RichTextAttributes.CheckEntryType(entryType.GetString()) is { } error)
                    {
                        Add($"{path}.entryType", error);
                    }

                    break;
            }
        }

        private void CheckOptionalString(ImmutableSortedDictionary<string, JsonElement> attributes, string name, string path)
        {
            if (attributes.TryGetValue(name, out var value) && value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            {
                Add($"{path}.{name}", $"The {name} must be a string.");
            }
        }

        private void CheckRequiredGuid(ImmutableSortedDictionary<string, JsonElement> attributes, string name, string path)
        {
            if (!attributes.TryGetValue(name, out var value)
                || value.ValueKind != JsonValueKind.String
                || !Guid.TryParseExact(value.GetString(), "D", out _))
            {
                Add($"{path}.{name}", $"The {name} must be a GUID.");
            }
        }

        private void Add(string path, string message) => errors.Add(new RichTextValidationError(path, message));

        private static RichTextNode Create(
            NodeSpec spec,
            ImmutableSortedDictionary<string, JsonElement> attributes,
            List<RichTextNode> children,
            List<RichTextMark> marks,
            string? text) =>
            spec.Type switch
            {
                RichTextNodeTypes.Doc => new DocumentNode(attributes, children),
                RichTextNodeTypes.Paragraph => new ParagraphNode(attributes, children),
                RichTextNodeTypes.Heading => new HeadingNode(attributes, children),
                RichTextNodeTypes.BulletList => new BulletListNode(attributes, children),
                RichTextNodeTypes.OrderedList => new OrderedListNode(attributes, children),
                RichTextNodeTypes.ListItem => new ListItemNode(attributes, children),
                RichTextNodeTypes.Blockquote => new BlockquoteNode(attributes, children),
                RichTextNodeTypes.CodeBlock => new CodeBlockNode(attributes, children),
                RichTextNodeTypes.HorizontalRule => new HorizontalRuleNode(attributes),
                RichTextNodeTypes.HardBreak => new HardBreakNode(attributes, marks),
                RichTextNodeTypes.Text => new TextNode(attributes, text!, marks),
                RichTextNodeTypes.Media => new MediaNode(attributes, marks),
                _ => new InlineBlockNode(attributes, marks),
            };
    }
}
