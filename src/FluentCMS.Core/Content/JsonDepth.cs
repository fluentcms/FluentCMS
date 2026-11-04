using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FluentCMS.Core.Content;

// The deepest JSON that a rich text document and every other value of an entry are accepted at, so that nothing one of them
// accepts is refused by another.
internal static class JsonDepth
{
    public const int Max = 128;

    // What is stored and read back holds a value in the object of the fields of its entry, one level deeper than the value.
    public const int Stored = Max + 1;

    public static readonly JsonDocumentOptions Value = new() { MaxDepth = Max };

    public static readonly JsonDocumentOptions Document = new() { MaxDepth = Stored };

    public static readonly JsonSerializerOptions Serializer = new() { MaxDepth = Stored, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static JsonNode? Parse(string json) => JsonNode.Parse(json, documentOptions: Document);

    public static JsonNode? ParseValue(string json) => JsonNode.Parse(json, documentOptions: Value);

    public static JsonElement ToElement(JsonNode node) => JsonSerializer.SerializeToElement(node, Serializer);

    public static bool Exceeds(JsonNode? node, int depth = 0) => node switch
    {
        JsonObject properties => ++depth > Max || properties.Any(property => Exceeds(property.Value, depth)),
        JsonArray items => ++depth > Max || items.Any(item => Exceeds(item, depth)),
        _ => false,
    };
}
