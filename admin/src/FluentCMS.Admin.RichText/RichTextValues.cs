using System.Text.Json.Nodes;

namespace FluentCMS.Admin.RichText;

internal static class RichTextValues
{
    // Two documents are the same when they differ at most in the order of a text's marks, which the editor sorts by its
    // schema and the server keeps as written; a node holds each mark type once, so the type orders them.
    public static bool Same(JsonNode? left, JsonNode? right) =>
        JsonNode.DeepEquals(Normalized(left), Normalized(right));

    private static JsonNode? Normalized(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject item:
                var copy = new JsonObject();
                foreach (var (name, value) in item)
                {
                    copy[name] = name == "marks" && value is JsonArray marks
                        ? new JsonArray([.. marks.Select(Normalized).OrderBy(mark => mark?["type"]?.ToJsonString(), StringComparer.Ordinal)])
                        : Normalized(value);
                }

                return copy;
            case JsonArray items:
                return new JsonArray([.. items.Select(Normalized)]);
            default:
                return node?.DeepClone();
        }
    }
}
