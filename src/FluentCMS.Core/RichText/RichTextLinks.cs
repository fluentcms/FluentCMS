using System.Text.Json.Nodes;
using FluentCMS.Core.Content;
using FluentCMS.Schema;

namespace FluentCMS.Core.ProseMirror;

// A link whose href SafeUrl refuses, such as javascript:, is refused when written. One stored before that rule is left in
// the row and dropped from what a read returns, keeping its text, so a front end that renders the JSON never receives it.
internal static class RichTextLinks
{
    public static IEnumerable<string> UnsafeHrefPaths(DocumentNode document) => UnsafeHrefPaths(document, "$");

    public static RichText WithoutUnsafe(RichText document)
    {
        if (!MayHold(document.Json) || JsonDepth.Parse(document.Json) is not { } node || !DropUnsafe(node))
        {
            return document;
        }

        return new RichText(EntryJson.Text(node));
    }

    public static bool DropUnsafe(JsonNode? node)
    {
        if (node is not JsonObject item)
        {
            return false;
        }

        var changed = false;
        if (item["marks"] is JsonArray marks)
        {
            var unsafeLinks = marks.OfType<JsonObject>().Where(IsUnsafeLink).ToList();
            foreach (var mark in unsafeLinks)
            {
                marks.Remove(mark);
            }

            changed = unsafeLinks.Count > 0;
            if (changed && marks.Count == 0)
            {
                item.Remove("marks");
            }
        }

        if (item["content"] is JsonArray content)
        {
            foreach (var child in content)
            {
                changed |= DropUnsafe(child);
            }
        }

        return changed;
    }

    private static bool MayHold(string json) => json.Contains($"\"{RichTextMarkTypes.Link}\"", StringComparison.Ordinal);

    private static IEnumerable<string> UnsafeHrefPaths(RichTextNode node, string path)
    {
        for (var index = 0; index < node.Marks.Count; index++)
        {
            if (node.Marks[index] is LinkMark link && !SafeUrl.IsSafe(link.Href))
            {
                yield return $"{path}.marks[{index}].attrs.href";
            }
        }

        for (var index = 0; index < node.Content.Count; index++)
        {
            foreach (var found in UnsafeHrefPaths(node.Content[index], $"{path}.content[{index}]"))
            {
                yield return found;
            }
        }
    }

    private static bool IsUnsafeLink(JsonObject mark) =>
        mark["type"] is JsonValue type && type.TryGetValue<string>(out var name) && name == RichTextMarkTypes.Link
        && mark["attrs"] is JsonObject attributes
        && attributes["href"] is JsonValue href && href.TryGetValue<string>(out var url)
        && !SafeUrl.IsSafe(url);
}
