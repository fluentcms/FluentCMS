using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentCMS.Schema;

namespace FluentCMS.Core.ProseMirror;

public static class RichTextSerializer
{
    private static readonly JsonWriterOptions options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
    };

    public static string Serialize(DocumentNode document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, options))
        {
            WriteNode(writer, document);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    public static RichText ToRichText(DocumentNode document) => new(Serialize(document));

    internal static string Compact(JsonElement element)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, options))
        {
            element.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteNode(Utf8JsonWriter writer, RichTextNode node)
    {
        writer.WriteStartObject();
        writer.WriteString("type", node.Type);
        WriteAttributes(writer, node.Attributes);

        if (node.Content.Count > 0)
        {
            writer.WriteStartArray("content");
            foreach (var child in node.Content)
            {
                WriteNode(writer, child);
            }

            writer.WriteEndArray();
        }

        if (node.Marks.Count > 0)
        {
            writer.WriteStartArray("marks");
            foreach (var mark in node.Marks)
            {
                writer.WriteStartObject();
                writer.WriteString("type", mark.Type);
                WriteAttributes(writer, mark.Attributes);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        if (node is TextNode text)
        {
            writer.WriteString("text", text.Text);
        }

        writer.WriteEndObject();
    }

    private static void WriteAttributes(Utf8JsonWriter writer, IReadOnlyDictionary<string, JsonElement> attributes)
    {
        if (attributes.Count == 0)
        {
            return;
        }

        writer.WriteStartObject("attrs");
        foreach (var (name, value) in attributes)
        {
            writer.WritePropertyName(name);
            value.WriteTo(writer);
        }

        writer.WriteEndObject();
    }
}
