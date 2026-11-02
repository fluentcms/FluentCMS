namespace FluentCMS.Schema;

/// <summary>
/// A rich text value: a ProseMirror document held as JSON text. Two values are equal when their
/// JSON text is identical. Parsing and rendering live in the engine.
/// </summary>
public sealed record RichText
{
    private const string EmptyDocument = """{"type":"doc","content":[{"type":"paragraph"}]}""";

    /// <summary>Creates a rich text value from a ProseMirror document.</summary>
    /// <param name="json">The document as JSON text.</param>
    public RichText(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        Json = json;
    }

    /// <summary>A document with a single empty paragraph.</summary>
    public static RichText Empty { get; } = new(EmptyDocument);

    /// <summary>The document as JSON text.</summary>
    public string Json { get; }
}
