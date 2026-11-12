using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace FluentCMS.Core.Schema;

// What `schema diff` and `migrations add` print: the changes in the words of the drift log, each destructive one marked,
// and the possible renames with the attribute that would make each one.
internal sealed record SchemaDiffReport(IReadOnlyList<SchemaChange> Changes, IReadOnlyList<PossibleRename> PossibleRenames)
{
    public const string DestructiveMark = " [destructive]";

    public bool HasChanges => Changes.Count > 0;

    public IReadOnlyList<SchemaChange> Destructive => [.. Changes.Where(change => change.IsDestructive)];

    public string Summary => Changes.Count switch
    {
        0 => "no difference",
        1 => $"1 difference{DestructiveCount()}",
        _ => $"{Changes.Count} differences{DestructiveCount()}",
    };

    public async Task WriteTextAsync(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (!HasChanges)
        {
            await output.WriteLineAsync("No difference.");
            return;
        }

        foreach (var change in Changes)
        {
            await output.WriteLineAsync(change.IsDestructive ? $"{change}{DestructiveMark}" : change.ToString());
        }

        foreach (var rename in PossibleRenames)
        {
            await output.WriteLineAsync(rename.ToString());
        }
    }

    // One line: {"changes":[{"kind","text","destructive",...}],"possibleRenames":[{"type","from","to","attribute"}]}.
    public string ToJson()
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            json.WriteStartObject();
            json.WriteStartArray("changes");
            foreach (var change in Changes)
            {
                json.WriteStartObject();
                json.WriteString("kind", JsonNamingPolicy.CamelCase.ConvertName(change.Kind.ToString()));
                json.WriteString("family", change.Family);
                json.WriteString("type", change.Type);
                WriteOptional(json, "field", change.Field);
                WriteOptional(json, "formerField", change.FormerField);
                if (change.Properties is { Count: > 0 } properties)
                {
                    json.WriteStartArray("properties");
                    foreach (var property in properties)
                    {
                        json.WriteStringValue(property);
                    }

                    json.WriteEndArray();
                }

                json.WriteBoolean("destructive", change.IsDestructive);
                json.WriteString("text", change.ToString());
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteStartArray("possibleRenames");
            foreach (var rename in PossibleRenames)
            {
                json.WriteStartObject();
                json.WriteString("family", rename.Family);
                json.WriteString("type", rename.Type);
                json.WriteString("from", rename.From);
                json.WriteString("to", rename.To);
                json.WriteString("attribute", rename.Attribute);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private string DestructiveCount() => Destructive.Count == 0 ? "" : $", {Destructive.Count} destructive";

    private static void WriteOptional(Utf8JsonWriter json, string name, string? value)
    {
        if (value is not null)
        {
            json.WriteString(name, value);
        }
    }
}
