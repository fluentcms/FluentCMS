using System.Text.Json;

namespace FluentCMS.Core.Schema;

// One field of a content type definition. Not a record: settings are JSON, which has no value equality.
public sealed class FieldDefinition
{
    public FieldDefinition(string name, string type, bool required = false, JsonElement? settings = null, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(type);

        var resolved = settings is { ValueKind: not (JsonValueKind.Undefined or JsonValueKind.Null) } given ? given : EmptySettings;
        if (resolved.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Field settings must be a JSON object.", nameof(settings));
        }

        Name = name;
        Type = type;
        Required = required;
        Description = description;
        // A clone outlives the document it was read from.
        Settings = resolved.Clone();
    }

    public static JsonElement EmptySettings { get; } = CreateEmptySettings();

    public string Name { get; }

    public string Type { get; }

    public bool Required { get; }

    // What the field means, in the author's words; the schema document shows it to agents (SCH-06).
    public string? Description { get; }

    // Read by the field type named in Type; always a JSON object.
    public JsonElement Settings { get; }

    private static JsonElement CreateEmptySettings()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }
}
