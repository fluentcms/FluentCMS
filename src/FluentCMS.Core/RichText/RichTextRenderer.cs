using FluentCMS.Schema;

namespace FluentCMS.Core.ProseMirror;

public sealed class RichTextRenderer : IRichTextRenderer
{
    private static readonly string[] formats = [RichTextFormats.Html, RichTextFormats.Markdown];

    private readonly Dictionary<string, IRichTextNodeRenderer> nodeRenderers = new(StringComparer.Ordinal);

    public RichTextRenderer(IEnumerable<IRichTextNodeRenderer>? nodeRenderers = null)
    {
        foreach (var renderer in nodeRenderers ?? [])
        {
            Register(renderer);
        }
    }

    public IReadOnlyCollection<string> Formats => formats;

    public string Render(RichText value, string format, RichTextRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        var normalized = Normalize(format);
        return Create(normalized, options).Render(RichTextParser.Parse(value));
    }

    public string Render(DocumentNode document, string format, RichTextRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var normalized = Normalize(format);

        var errors = RichTextValidator.Validate(document);
        if (errors.Count > 0)
        {
            throw new RichTextValidationException(errors);
        }

        return Create(normalized, options).Render(document);
    }

    private void Register(IRichTextNodeRenderer renderer)
    {
        var nodeType = renderer.NodeType;
        if (string.IsNullOrWhiteSpace(nodeType))
        {
            throw new ArgumentException($"{renderer.GetType().Name} has no node type.", nameof(renderer));
        }

        if (nodeType is RichTextNodeTypes.Doc or RichTextNodeTypes.Text or RichTextNodeTypes.InlineBlock)
        {
            throw new ArgumentException($"A renderer cannot be registered for '{nodeType}'; register it for a block type name or another node type.", nameof(renderer));
        }

        if (!nodeRenderers.TryAdd(nodeType, renderer))
        {
            throw new ArgumentException($"More than one renderer is registered for '{nodeType}'.", nameof(renderer));
        }
    }

    private FormatRenderer Create(string format, RichTextRenderOptions? options)
    {
        options ??= new RichTextRenderOptions();
        return format == RichTextFormats.Html
            ? new HtmlRenderer(nodeRenderers, options)
            : new MarkdownRenderer(nodeRenderers, options);
    }

    private static string Normalize(string format)
    {
        ArgumentNullException.ThrowIfNull(format);

        return formats.FirstOrDefault(known => string.Equals(known, format, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"The format '{format}' is not supported. Supported formats: {string.Join(", ", formats)}.", nameof(format));
    }
}
