using System.Buffers;
using System.Text.Json;
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
