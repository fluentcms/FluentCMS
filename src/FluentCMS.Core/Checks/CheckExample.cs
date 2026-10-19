using System.Text.Json;

namespace FluentCMS.Core.Checks;

// A piece of content and the verdict the check must give it; a check is activated only once all of its examples get theirs
// (CHK-02). Not a record: the fields are JSON, which has no value equality.
public sealed class CheckExample
{
    public const int MaxNameLength = 200;

    public CheckExample(string name, CheckVerdict expected, JsonElement fields, IEnumerable<string>? sources = null, string? contentType = null)
    {
        ArgumentNullException.ThrowIfNull(name);

        Name = name;
        Expected = expected;
        // A clone outlives the document it was read from; an element that holds nothing stays as it is, for Validate to refuse.
        Fields = fields.ValueKind == JsonValueKind.Undefined ? fields : fields.Clone();
        Sources = [.. sources ?? []];
        ContentType = contentType;
    }

    // Names the example in the error when it does not get its verdict, such as "says best in class".
    public string Name { get; }

    // Pass or fail; an example that needs review proves nothing about the check.
    public CheckVerdict Expected { get; }

    public JsonElement Fields { get; }

    public IReadOnlyList<string> Sources { get; }

    // The slug of the content type the fields belong to, for types that read the definition; null for content of no type.
    public string? ContentType { get; }
}
