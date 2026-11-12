using System.Text.Json.Nodes;
using FluentCMS.Content;

namespace FluentCMS.Core.Transfer;

// Whether the instance can hold the content of a package (spec 4.14): its schema hash is the package's, or the instance has
// every type the package brings, with the same kind, scope and localization, and every field of those types and of the
// components and blocks they use, with the same field type, multiplicity, localization and target. More types or fields in
// the instance are fine. Both schemas are compared as the snapshots of 4.2.
internal static class SchemaCompatibility
{
    private static readonly string[] ContentGroups = ["collections", "singletons"];
    private static readonly string[] PartGroups = ["components", "blocks"];
    private static readonly string[] FieldKeys = ["type", "multiple", "localizable", "target", "component"];

    public static List<ValidationError> Check(string packageSchema, string instanceSchema, IEnumerable<string> types)
    {
        List<ValidationError> errors = [];
        var package = Index(JsonNode.Parse(packageSchema) as JsonObject);
        var instance = Index(JsonNode.Parse(instanceSchema) as JsonObject);
        HashSet<string> parts = new(StringComparer.Ordinal);
        Queue<string> pending = [];

        foreach (var name in types)
        {
            if (!package.TryGetValue(name, out var mine))
            {
                errors.Add(new ValidationError($"types.{name}", TransferErrorCodes.SchemaIncompatible, $"The schema of the package has no content type '{name}'."));
                continue;
            }

            CompareType(name, mine, instance.GetValueOrDefault(name), errors, part => Enqueue(part, parts, pending));
        }

        while (pending.TryDequeue(out var part))
        {
            if (package.TryGetValue(part, out var mine))
            {
                CompareType(part, mine, instance.GetValueOrDefault(part), errors, inner => Enqueue(inner, parts, pending));
            }
        }

        return errors;
    }

    private static void Enqueue(string part, HashSet<string> parts, Queue<string> pending)
    {
        if (parts.Add(part))
        {
            pending.Enqueue(part);
        }
    }

    private static void CompareType(string name, (string Group, JsonObject Type) mine, (string Group, JsonObject Type) theirs, List<ValidationError> errors, Action<string> uses)
    {
        var path = $"types.{name}";
        if (theirs.Type is null)
        {
            errors.Add(new ValidationError(path, TransferErrorCodes.SchemaIncompatible, $"The instance has no type '{name}', which the package brings."));
            return;
        }

        if (mine.Group != theirs.Group || !Same(mine.Type, theirs.Type, "scope") || !Same(mine.Type, theirs.Type, "localized"))
        {
            errors.Add(new ValidationError(path, TransferErrorCodes.SchemaIncompatible, $"The type '{name}' differs in kind, scope or localization between the package and the instance."));
        }

        var fields = (theirs.Type["fields"] as JsonArray ?? []).OfType<JsonObject>().ToDictionary(field => (string)field["name"]!, StringComparer.Ordinal);
        foreach (var field in (mine.Type["fields"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var fieldName = (string)field["name"]!;
            if (!fields.TryGetValue(fieldName, out var other))
            {
                errors.Add(new ValidationError($"{path}.{fieldName}", TransferErrorCodes.SchemaIncompatible, $"The instance has no field '{fieldName}' in '{name}'."));
                continue;
            }

            if (FieldKeys.Any(key => !Same(field, other, key)) || !Covers(Blocks(other), Blocks(field)))
            {
                errors.Add(new ValidationError($"{path}.{fieldName}", TransferErrorCodes.SchemaIncompatible, $"The field '{fieldName}' of '{name}' differs in type, multiplicity, localization or target between the package and the instance."));
            }

            if (field["component"] is JsonValue component)
            {
                uses((string)component!);
            }

            foreach (var block in Blocks(field)?.OfType<JsonValue>() ?? [])
            {
                uses((string)block!);
            }
        }
    }

    private static JsonArray? Blocks(JsonObject field) => field["blocks"] as JsonArray;

    // The instance may allow more blocks in a field than the package uses.
    private static bool Covers(JsonArray? instance, JsonArray? package) =>
        package is null || (instance is not null && package.All(block => instance.Any(other => JsonNode.DeepEquals(block, other))));

    private static bool Same(JsonObject left, JsonObject right, string key) => JsonNode.DeepEquals(left[key], right[key]);

    private static Dictionary<string, (string Group, JsonObject Type)> Index(JsonObject? schema)
    {
        Dictionary<string, (string Group, JsonObject Type)> types = new(StringComparer.Ordinal);
        foreach (var group in ContentGroups.Concat(PartGroups))
        {
            foreach (var type in (schema?[group] as JsonArray ?? []).OfType<JsonObject>())
            {
                types[(string)type["name"]!] = (group, type);
            }
        }

        return types;
    }
}
