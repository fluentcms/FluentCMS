using System.Buffers;
using System.Text.Json;
using FluentCMS.Core.Media;
using FluentCMS.Core.Rules;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.ChangeSets;

// What one operation changes, from the version it was based on to the version it proposes (CHG-03). Properties belong to the
// item as a whole and fields to its fields; both list only what differs.
public sealed class ItemDiff
{
    public const string EntityProperty = "entity";

    // Set only when fields kept in both versions changed their relative order, so a pure reordering does not read as no change.
    public const string FieldOrderProperty = "fieldOrder";

    private ItemDiff(ChangeOperation operation, IReadOnlyList<ValueDiff> properties, IReadOnlyList<ValueDiff> fields)
    {
        Operation = operation;
        Properties = properties;
        Fields = fields;
    }

    public ChangeOperation Operation { get; }

    public IReadOnlyList<ValueDiff> Properties { get; }

    public IReadOnlyList<ValueDiff> Fields { get; }

    // The base definition is absent for a create and the proposed one for a delete, as the operation's versions say. A field's
    // value is its definition without the name.
    public static ItemDiff ForSchema(ChangeOperation operation, ContentTypeDefinition? baseDefinition, ContentTypeDefinition? proposedDefinition)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (operation.ItemKind != ItemKind.Schema)
        {
            throw new ArgumentException($"A schema diff is for a schema operation, and this one is on a {operation.ItemKind} item.", nameof(operation));
        }

        if ((operation.BaseVersion is null) != (baseDefinition is null) || (operation.ProposedVersion is null) != (proposedDefinition is null))
        {
            throw new ArgumentException("A schema diff takes a definition for each version the operation names, and only for those.");
        }

        var properties = ValueDiff.Compare(SchemaProperties(baseDefinition, proposedDefinition), SchemaProperties(proposedDefinition, baseDefinition));
        var fields = ValueDiff.Compare(FieldValues(baseDefinition), FieldValues(proposedDefinition));

        return new ItemDiff(operation, properties, fields);
    }

    // A rule's kind, text, severity and each list of its scope, so a reviewer sees what the rule says and where it applies before and
    // after (RUL-01). The base definition is absent for a create and the proposed one for a retirement.
    public static ItemDiff ForRule(ChangeOperation operation, RuleDefinition? baseDefinition, RuleDefinition? proposedDefinition)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (operation.ItemKind != ItemKind.Rule)
        {
            throw new ArgumentException($"A rule diff is for a rule operation, and this one is on a {operation.ItemKind} item.", nameof(operation));
        }

        if ((operation.BaseVersion is null) != (baseDefinition is null) || (operation.ProposedVersion is null) != (proposedDefinition is null))
        {
            throw new ArgumentException("A rule diff takes a definition for each version the operation names, and only for those.");
        }

        return new ItemDiff(operation, ValueDiff.Compare(RuleProperties(baseDefinition), RuleProperties(proposedDefinition)), []);
    }

    // The base fields are absent for a create and the proposed ones for a delete, as the operation's versions say. A field's value is
    // what the entry stores under its name, which on an entity is the whole fact. Fields follow the order of the definition given,
    // the one the shown version was written against, so the diff reads the same on every database.
    public static ItemDiff ForEntry(
        ChangeOperation operation,
        JsonElement? baseFields,
        JsonElement? proposedFields,
        ContentTypeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(definition);

        if (operation.ItemKind is not (ItemKind.Entry or ItemKind.Entity))
        {
            throw new ArgumentException($"An entry diff is for an entry or entity operation, and this one is on a {operation.ItemKind} item.", nameof(operation));
        }

        if ((operation.BaseVersion is null) != (baseFields is null) || (operation.ProposedVersion is null) != (proposedFields is null))
        {
            throw new ArgumentException("An entry diff takes fields for each version the operation names, and only for those.");
        }

        var order = definition.Fields.Select((field, index) => (field.Name, index)).ToDictionary(field => field.Name, field => field.index, StringComparer.Ordinal);

        return new ItemDiff(operation, [], ValueDiff.Compare(EntryValues(baseFields, order), EntryValues(proposedFields, order)));
    }

    // The base version is absent for a create and the proposed one for a delete, as the operation's versions say. The metadata
    // people and agents write are fields; the bytes, which a metadata change keeps, are properties.
    public static ItemDiff ForMedia(ChangeOperation operation, MediaAssetVersion? baseVersion, MediaAssetVersion? proposedVersion)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (operation.ItemKind != ItemKind.Media)
        {
            throw new ArgumentException($"A media diff is for a media operation, and this one is on a {operation.ItemKind} item.", nameof(operation));
        }

        if ((operation.BaseVersion is null) != (baseVersion is null) || (operation.ProposedVersion is null) != (proposedVersion is null))
        {
            throw new ArgumentException("A media diff takes a version for each version the operation names, and only for those.");
        }

        return new ItemDiff(
            operation,
            ValueDiff.Compare(MediaProperties(baseVersion), MediaProperties(proposedVersion)),
            ValueDiff.Compare(MediaFields(baseVersion), MediaFields(proposedVersion)));
    }

    private static IEnumerable<KeyValuePair<string, JsonElement>> MediaProperties(MediaAssetVersion? version)
    {
        return version is null
            ? []
            :
            [
                new("contentType", JsonSerializer.SerializeToElement(version.Blob.ContentType)),
                new("length", JsonSerializer.SerializeToElement(version.Blob.Length)),
                new("sha256", JsonSerializer.SerializeToElement(version.Blob.Sha256)),
            ];
    }

    // Alt text and a caption that are absent are absent here too, so adding one reads as added.
    private static IEnumerable<KeyValuePair<string, JsonElement>> MediaFields(MediaAssetVersion? version)
    {
        if (version is null)
        {
            yield break;
        }

        var metadata = version.Metadata;
        yield return new("fileName", JsonSerializer.SerializeToElement(metadata.FileName));
        yield return new("folder", JsonSerializer.SerializeToElement(metadata.Folder));
        yield return new("tags", JsonSerializer.SerializeToElement(metadata.Tags));

        if (metadata.AltText is { } altText)
        {
            yield return new("altText", JsonSerializer.SerializeToElement(altText));
        }

        if (metadata.Caption is { } caption)
        {
            yield return new("caption", JsonSerializer.SerializeToElement(caption));
        }
    }

    // A field the definition no longer has goes last, by name.
    private static IEnumerable<KeyValuePair<string, JsonElement>> EntryValues(JsonElement? fields, Dictionary<string, int> order)
    {
        return fields?.EnumerateObject()
            .Select(property => new KeyValuePair<string, JsonElement>(property.Name, property.Value))
            .OrderBy(field => order.GetValueOrDefault(field.Key, int.MaxValue))
            .ThenBy(field => field.Key, StringComparer.Ordinal)
            .ToList() ?? [];
    }

    private static List<KeyValuePair<string, JsonElement>> SchemaProperties(ContentTypeDefinition? definition, ContentTypeDefinition? other)
    {
        if (definition is null)
        {
            return [];
        }

        var properties = new List<KeyValuePair<string, JsonElement>>
        {
            new(EntityProperty, Write(writer => writer.WriteBooleanValue(definition.IsEntity))),
        };

        if (other is not null && !KeptFields(definition, other).SequenceEqual(KeptFields(other, definition), StringComparer.Ordinal))
        {
            properties.Add(new(FieldOrderProperty, Write(writer =>
            {
                writer.WriteStartArray();
                foreach (var field in definition.Fields)
                {
                    writer.WriteStringValue(field.Name);
                }

                writer.WriteEndArray();
            })));
        }

        return properties;
    }

    // The names of the definition's fields that the other definition also has, in the definition's order.
    private static IEnumerable<string> KeptFields(ContentTypeDefinition definition, ContentTypeDefinition other)
    {
        return definition.Fields.Select(field => field.Name).Where(name => other.Fields.Any(field => field.Name == name));
    }

    private static IEnumerable<KeyValuePair<string, JsonElement>> FieldValues(ContentTypeDefinition? definition)
    {
        return definition?.Fields.Select(field => new KeyValuePair<string, JsonElement>(field.Name, Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("type", field.Type);
            writer.WriteBoolean("required", field.Required);
            writer.WritePropertyName("settings");
            field.Settings.WriteTo(writer);
            writer.WriteEndObject();
        }))) ?? [];
    }

    private static List<KeyValuePair<string, JsonElement>> RuleProperties(RuleDefinition? definition)
    {
        if (definition is null)
        {
            return [];
        }

        using var document = JsonDocument.Parse(definition.ToJson());
        var root = document.RootElement;
        var properties = new List<KeyValuePair<string, JsonElement>>
        {
            new("kind", root.GetProperty("kind").Clone()),
            new("text", root.GetProperty("text").Clone()),
            new("severity", root.GetProperty("severity").Clone()),
        };

        if (root.TryGetProperty("scope", out var scope))
        {
            properties.AddRange(scope.EnumerateObject().Select(list => new KeyValuePair<string, JsonElement>($"scope.{list.Name}", list.Value.Clone())));
        }

        return properties;
    }

    private static JsonElement Write(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            write(writer);
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }
}
