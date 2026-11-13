using System.Text.Json.Nodes;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Schema;

namespace FluentCMS.Core.Content;

// A version holds an entry's fields under the names they had when it was written. A field renamed since, and a field of a
// single component renamed since, is read under its name, else under each of its former names in order, as a content
// migration renames their columns. A field kept in JSON inside another field keeps its former name, as the stored entry does.
internal static class VersionSnapshot
{
    public static JsonObject Parse(SchemaModel schema, SchemaType type, string snapshot) =>
        JsonDepth.Parse(snapshot) is JsonObject fields ? UnderCurrentNames(schema, type, fields) : [];

    // Keeps the order of the keys, and the keys no field takes.
    private static JsonObject UnderCurrentNames(SchemaModel schema, SchemaType owner, JsonObject stored)
    {
        Dictionary<string, string> renamed = new(StringComparer.Ordinal);
        foreach (var field in owner.Fields.Where(field => !stored.ContainsKey(field.Name)))
        {
            if (field.FormerNames.FirstOrDefault(name => stored.ContainsKey(name) && !renamed.ContainsKey(name)) is { } former)
            {
                renamed[former] = field.Name;
            }
        }

        List<KeyValuePair<string, JsonNode?>> pairs = [.. stored];
        stored.Clear();
        JsonObject fields = [];
        foreach (var (key, value) in pairs)
        {
            var name = renamed.GetValueOrDefault(key, key);
            fields[name] = value is JsonObject group && Flattened(schema, owner.FindField(name)) is { } component
                ? UnderCurrentNames(schema, component, group)
                : value;
        }

        return fields;
    }

    private static SchemaType? Flattened(SchemaModel schema, SchemaField? field) =>
        field is { FieldType: FieldTypeNames.Component } && schema.FieldTypes.Find(field.FieldType)?.StorageKind(field) == FieldStorageKind.Column
            ? schema.FindComponent((string)field.Settings[FieldSettings.Component])
            : null;
}
