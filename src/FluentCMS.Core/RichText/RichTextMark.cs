using System.Collections.Immutable;
using System.Text.Json;

namespace FluentCMS.Core.ProseMirror;

public abstract class RichTextMark
{
    private protected RichTextMark(string type, ImmutableSortedDictionary<string, JsonElement>? attributes)
    {
        Type = type;
        Attributes = attributes ?? RichTextAttributes.Empty;
    }

    public string Type { get; }

    public IReadOnlyDictionary<string, JsonElement> Attributes { get; }

    internal bool SameAs(RichTextMark other)
    {
        if (Type != other.Type || Attributes.Count != other.Attributes.Count)
        {
            return false;
        }

        foreach (var (name, value) in Attributes)
        {
            if (!other.Attributes.TryGetValue(name, out var otherValue) || value.GetRawText() != otherValue.GetRawText())
            {
                return false;
            }
        }

        return true;
    }

    private protected string? OptionalString(string name) =>
        Attributes.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

public sealed class BoldMark : RichTextMark
{
    public BoldMark()
        : base(RichTextMarkTypes.Bold, null)
    {
    }

    internal BoldMark(ImmutableSortedDictionary<string, JsonElement> attributes)
        : base(RichTextMarkTypes.Bold, attributes)
    {
    }
}

public sealed class ItalicMark : RichTextMark
{
    public ItalicMark()
        : base(RichTextMarkTypes.Italic, null)
    {
    }

    internal ItalicMark(ImmutableSortedDictionary<string, JsonElement> attributes)
        : base(RichTextMarkTypes.Italic, attributes)
    {
    }
}

public sealed class UnderlineMark : RichTextMark
{
    public UnderlineMark()
        : base(RichTextMarkTypes.Underline, null)
    {
    }

    internal UnderlineMark(ImmutableSortedDictionary<string, JsonElement> attributes)
        : base(RichTextMarkTypes.Underline, attributes)
    {
    }
}

public sealed class StrikeMark : RichTextMark
{
    public StrikeMark()
        : base(RichTextMarkTypes.Strike, null)
    {
    }

    internal StrikeMark(ImmutableSortedDictionary<string, JsonElement> attributes)
        : base(RichTextMarkTypes.Strike, attributes)
    {
    }
}

public sealed class CodeMark : RichTextMark
{
    public CodeMark()
        : base(RichTextMarkTypes.Code, null)
    {
    }

    internal CodeMark(ImmutableSortedDictionary<string, JsonElement> attributes)
        : base(RichTextMarkTypes.Code, attributes)
    {
    }
}

public sealed class LinkMark : RichTextMark
{
    public LinkMark(string href, string? title = null, string? target = null)
        : base(RichTextMarkTypes.Link, BuildAttributes(href, title, target))
    {
    }

    internal LinkMark(ImmutableSortedDictionary<string, JsonElement> attributes)
        : base(RichTextMarkTypes.Link, attributes)
    {
    }

    public string Href => Attributes["href"].GetString()!;

    public string? Title => OptionalString("title");

    public string? Target => OptionalString("target");

    private static ImmutableSortedDictionary<string, JsonElement> BuildAttributes(string href, string? title, string? target)
    {
        ArgumentNullException.ThrowIfNull(href);

        var attributes = new List<(string Name, JsonElement Value)> { ("href", RichTextAttributes.String(href)) };
        if (title is not null)
        {
            attributes.Add(("title", RichTextAttributes.String(title)));
        }

        if (target is not null)
        {
            attributes.Add(("target", RichTextAttributes.String(target)));
        }

        return RichTextAttributes.Create([.. attributes]);
    }
}

public sealed class EntryLinkMark : RichTextMark
{
    public EntryLinkMark(Guid entryId, string entryType)
        : base(RichTextMarkTypes.EntryLink, BuildAttributes(entryId, entryType))
    {
    }

    internal EntryLinkMark(ImmutableSortedDictionary<string, JsonElement> attributes)
        : base(RichTextMarkTypes.EntryLink, attributes)
    {
    }

    public Guid EntryId => Guid.Parse(Attributes["entryId"].GetString()!);

    public string EntryType => Attributes["entryType"].GetString()!;

    private static ImmutableSortedDictionary<string, JsonElement> BuildAttributes(Guid entryId, string entryType)
    {
        if (RichTextAttributes.CheckEntryType(entryType) is { } error)
        {
            throw new ArgumentException(error, nameof(entryType));
        }

        return RichTextAttributes.Create(
            ("entryId", RichTextAttributes.String(entryId.ToString())),
            ("entryType", RichTextAttributes.String(entryType)));
    }
}
