using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Schema;

namespace FluentCMS.Core.ProseMirror;

// The inline blocks of a rich text document read as JSON, so that their data can be read and changed in place. It reads
// as deep a document as the rich text field type accepts, so that nothing it accepts goes unseen here.
internal static class InlineBlockJson
{
    private static readonly JsonDocumentOptions ReadOptions = new() { MaxDepth = 128 };
    private static readonly JsonSerializerOptions WriteOptions = new() { MaxDepth = 128, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static bool MayHold(string json) => json.Contains(RichTextNodeTypes.InlineBlock, StringComparison.Ordinal);

    public static JsonNode? Parse(string json) => JsonNode.Parse(json, documentOptions: ReadOptions);

    public static string Text(JsonNode node) => node.ToJsonString(WriteOptions);

    // The canonical document, for what was changed as JSON.
    public static RichText ToRichText(JsonNode document) => RichTextSerializer.ToRichText(RichTextParser.Parse(Text(document)));

    public static List<InlineBlockJsonNode> Find(JsonNode? document)
    {
        List<InlineBlockJsonNode> found = [];
        Visit(document, "", found);
        return found;
    }

    private static void Visit(JsonNode? node, string path, List<InlineBlockJsonNode> found)
    {
        if (node is not JsonObject item)
        {
            return;
        }

        if (item["type"] is JsonValue kind && kind.TryGetValue<string>(out var name) && name == RichTextNodeTypes.InlineBlock
            && item["attrs"] is JsonObject attributes
            && attributes["blockType"] is JsonValue type && type.TryGetValue<string>(out var blockType))
        {
            found.Add(new InlineBlockJsonNode(blockType, attributes, path));
        }

        if (item["content"] is JsonArray content)
        {
            for (var index = 0; index < content.Count; index++)
            {
                Visit(content[index], $"{path}.content[{index}]", found);
            }
        }
    }
}

// Path is where the node is in its document, without the "$" of RichTextReferences, to follow a field path.
internal sealed record InlineBlockJsonNode(string BlockType, JsonObject Attributes, string Path)
{
    public JsonObject? Data => Attributes["data"] as JsonObject;

    public string DataPath => $"{Path}.attrs.data";
}
