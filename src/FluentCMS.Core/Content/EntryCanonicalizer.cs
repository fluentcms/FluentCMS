using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.ProseMirror;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Core.Storage;
using FluentCMS.Media;
using FluentCMS.Schema;

namespace FluentCMS.Core.Content;

// Turns what a client, a mapper or a hook hands over into the one form the service works on and stores:
// a column field holds its CLR value, a reference a Guid or a Guid[], rich text its canonical RichText, a single
// component a ContentDocument or null, and anything stored as JSON a JsonNode. Calling it on its own result changes nothing.
// The placeholders of spec 4.3 go in here, so that hooks, validation and the write all see the same entry.
internal sealed class EntryCanonicalizer(SchemaModel schema)
{
    private const string Discriminator = "type";
    private const string IdKey = "id";

    private readonly BlockIds _blocks = new(schema);

    // mayDerive says whether a slug may be made from a source field. Where it may not, the slug keeps what stored holds:
    // a slug shows its source to whoever reads it, so it must not be how a principal learns what it may not read.
    public ContentDocument Canonicalize(SchemaType type, ContentDocument entry, Func<SchemaType, SchemaField, bool> mayDerive, ContentDocument? stored = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(entry);

        List<ValidationError> errors = [];
        ContentDocument result = [];
        foreach (var (key, value) in entry)
        {
            if (SystemField.Find(key) is not null)
            {
                result[key] = value;
            }
            else if (type.FindField(key) is null)
            {
                errors.Add(Invalid(key, $"'{key}' is not a field of {type.Name}."));
            }
        }

        try
        {
            ReadFields(type, entry, result, "", flattened: false, errors, mayDerive, stored);
            EntryJson.ThrowIfUnreadable(type, result);
            _blocks.CheckUnique(type, result, errors);
        }
        catch (ArgumentException exception)
        {
            // JSON that repeats a key only shows it when it is read.
            errors.Add(Invalid("", exception.Message));
        }

        return errors.Count == 0 ? result : throw new ValidationException(errors);
    }

    // One field on its own, to compare a client's value with the stored one. False when the value cannot be read.
    public bool TryRead(SchemaField field, object? raw, out object? value)
    {
        List<ValidationError> errors = [];
        value = Top(field, raw, field.Name, flattened: false, errors, NeverDerive, null);
        return errors.Count == 0;
    }

    private static bool NeverDerive(SchemaType owner, SchemaField source) => false;

    private void ReadFields(SchemaType owner, IEnumerable<KeyValuePair<string, object?>> values, ContentDocument into, string prefix, bool flattened, List<ValidationError> errors, Func<SchemaType, SchemaField, bool> mayDerive, ContentDocument? stored)
    {
        var known = values as ContentDocument ?? new ContentDocument(values);
        foreach (var field in owner.Fields)
        {
            known.TryGetValue(field.Name, out var raw);
            into[field.Name] = Top(field, raw, prefix + field.Name, flattened, errors, mayDerive, stored?.GetValueOrDefault(field.Name) as ContentDocument);
        }

        // After the loop, so that the source has its value whichever comes first.
        foreach (var field in owner.Fields.Where(field => field.FieldType == FieldTypeNames.Slug))
        {
            if (into[field.Name] is null or "" && Source(owner, field) is { } source && into.GetValueOrDefault(source.Name) is string text)
            {
                into[field.Name] = mayDerive(owner, source)
                    ? Derive(field, source, text, prefix + field.Name, errors)
                    : stored?.GetValueOrDefault(field.Name) ?? into[field.Name];
            }
        }
    }

    // Text with no letter or digit gives no slug. For a required slug the save says so, rather than leave it to Required
    // at publish; an optional slug may stay empty.
    private static string Derive(SchemaField field, SchemaField source, string text, string path, List<ValidationError> errors)
    {
        var slug = SlugFieldType.Slugify(text, FieldLengths.Max(field));
        if (slug.Length == 0 && field.Required && !string.IsNullOrWhiteSpace(text))
        {
            errors.Add(new ValidationError(path, ValidationErrorCodes.EmptySlug, $"No slug can be made from {source.Name}, which has no letter or digit. Send the slug."));
        }

        return slug;
    }

    private static SchemaField? Source(SchemaType owner, SchemaField field) =>
        field.Settings.TryGetValue(FieldSettings.Source, out var name) && name is string source ? owner.FindField(source) : null;

    private object? Top(SchemaField field, object? raw, string path, bool flattened, List<ValidationError> errors, Func<SchemaType, SchemaField, bool> mayDerive, ContentDocument? storedGroup)
    {
        var kind = schema.FieldTypes.Find(field.FieldType)!.StorageKind(field);
        if (kind == FieldStorageKind.Reference)
        {
            return ReadReference(field, raw, path, errors);
        }

        if (field.FieldType == FieldTypeNames.Component && !field.Multiple)
        {
            return ReadGroup(field, raw, path, errors, mayDerive, storedGroup);
        }

        if (kind == FieldStorageKind.Json && field.FieldType != FieldTypeNames.RichText)
        {
            return ToNode(field, raw, path, errors) ?? (IsList(field) ? new JsonArray() : null);
        }

        var value = Coerce(field, raw, path, errors);
        if (value is not null || flattened)
        {
            return value;
        }

        return field.FieldType switch
        {
            FieldTypeNames.RichText => RichText.Empty,
            FieldTypeNames.Slug => "",
            FieldTypeNames.Text or FieldTypeNames.LongText when field.Required => "",
            _ => null,
        };
    }

    private static bool IsList(SchemaField field) => field.Multiple || field.FieldType == FieldTypeNames.Blocks;

    private object? Coerce(SchemaField field, object? raw, string path, List<ValidationError> errors)
    {
        // The placeholder of a slug that has nothing to derive from comes back here, and the field type would refuse it.
        if (field.FieldType == FieldTypeNames.Slug && RawValue.Unwrap(raw) is "")
        {
            return null;
        }

        if (!schema.FieldTypes.Find(field.FieldType)!.TryCoerce(raw, field, out var value, out var message))
        {
            errors.Add(Invalid(path, $"The value {message}."));
            return null;
        }

        return field.FieldType == FieldTypeNames.RichText && value is RichText document ? CanonicalRichText(document, path, errors) : value;
    }

    private RichText? CanonicalRichText(RichText document, string path, List<ValidationError> errors)
    {
        if (RichTextParser.TryParse(document, out var parsed, out var problems))
        {
            var canonical = RichTextSerializer.ToRichText(parsed);
            return InlineBlockJson.MayHold(canonical.Json) ? WithBlockData(canonical, path, errors) : canonical;
        }

        errors.AddRange(problems.Select(problem => Invalid(path, $"The rich text is not valid at {problem.Path}: {problem.Message}")));
        return null;
    }

    // The data of an inline block is read as the same block in a block list. One of a type the schema does not know is
    // left as written, as the rich text contract has it.
    private RichText WithBlockData(RichText document, string path, List<ValidationError> errors)
    {
        var root = InlineBlockJson.Parse(document.Json)!;
        foreach (var inline in InlineBlockJson.Find(root))
        {
            if (schema.FindBlock(inline.BlockType) is { } block && inline.Data is { } data)
            {
                var map = data.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.Ordinal);
                var at = path + inline.DataPath;
                ReportUnknown(map, block, at, isBlock: false, errors);
                inline.Attributes["data"] = ReadObject(block, map, at, errors);
            }
        }

        return InlineBlockJson.ToRichText(root);
    }

    private ContentDocument? ReadGroup(SchemaField field, object? raw, string path, List<ValidationError> errors, Func<SchemaType, SchemaField, bool> mayDerive, ContentDocument? stored)
    {
        if (schema.FindComponent((string)field.Settings[FieldSettings.Component]) is not { } component)
        {
            errors.Add(Invalid(path, "The component is not part of the schema."));
            return null;
        }

        if (!TryReadMap(raw, out var map))
        {
            errors.Add(Invalid(path, "The value is not an object."));
            return null;
        }

        if (map is null)
        {
            return null;
        }

        ReportUnknown(map, component, path, isBlock: false, errors);
        ContentDocument group = [];
        ReadFields(component, map, group, path + ".", flattened: true, errors, mayDerive, stored);
        return group.Values.All(value => value is null or JsonArray { Count: 0 }) ? null : group;
    }

    // A value that lives inside JSON: inside a block or a repeated component, or the JSON field itself.
    private JsonNode? ToNode(SchemaField field, object? raw, string path, List<ValidationError> errors)
    {
        var kind = schema.FieldTypes.Find(field.FieldType)!.StorageKind(field);
        if (kind == FieldStorageKind.Reference)
        {
            return ReferenceNode(field, ReadReference(field, raw, path, errors));
        }

        switch (field.FieldType)
        {
            case FieldTypeNames.Blocks:
                return ReadObjects(field, raw, path, errors, ReadBlock);
            case FieldTypeNames.Component when field.Multiple:
                return ReadObjects(field, raw, path, errors, ReadComponentItem);
            case FieldTypeNames.Component:
                return ReadComponentItem(field, raw, path, errors);
            default:
                return EntryJson.ToNode(Coerce(field, raw, path, errors));
        }
    }

    private static JsonNode? ReferenceNode(SchemaField field, object? value)
    {
        JsonObject Stub(Guid id) => new() { [IdKey] = id.ToString("D"), [Discriminator] = EntryPaths.ReferenceTarget(field) };

        return value switch
        {
            Guid id => Stub(id),
            Guid[] ids => new JsonArray([.. ids.Select(id => (JsonNode)Stub(id))]),
            _ => null,
        };
    }

    private static JsonArray? ReadObjects(SchemaField field, object? raw, string path, List<ValidationError> errors, Func<SchemaField, object?, string, List<ValidationError>, JsonObject?> read)
    {
        if (!TryReadItems(raw, out var items))
        {
            errors.Add(Invalid(path, $"The value is not a list of {(field.FieldType == FieldTypeNames.Blocks ? "blocks" : "objects")}."));
            return null;
        }

        if (items is null)
        {
            return null;
        }

        JsonArray result = [];
        for (var index = 0; index < items.Count; index++)
        {
            result.Add(read(field, items[index], $"{path}[{index}]", errors));
        }

        return result;
    }

    private JsonObject? ReadBlock(SchemaField field, object? raw, string path, List<ValidationError> errors)
    {
        var allowed = SettingValues.List(field, FieldSettings.Blocks);
        if (!TryReadMap(raw, out var map) || map is null)
        {
            errors.Add(Invalid(path, $"The block is not an object with a \"{Discriminator}\" naming its block."));
            return null;
        }

        var name = BlockName(raw, map);
        if (name is null || !allowed.Contains(name) || schema.FindBlock(name) is not { } block)
        {
            errors.Add(new ValidationError(path, ValidationErrorCodes.NotAnAllowedOption, $"The block type {name ?? "(none)"} is not one of: {string.Join(", ", allowed)}."));
            return null;
        }

        ReportUnknown(map, block, path, isBlock: true, errors);
        var result = ReadObject(block, map, path, errors);
        JsonObject named = new() { [Discriminator] = name };
        if (BlockId(map, path, errors) is { } id)
        {
            named[BlockIds.Key] = id;
        }

        foreach (var pair in result.ToList())
        {
            result.Remove(pair.Key);
            named[pair.Key] = pair.Value;
        }

        return named;
    }

    // No id, or null, is a block the service gives an id to before it is stored.
    private static string? BlockId(Dictionary<string, object?> map, string path, List<ValidationError> errors)
    {
        if (!map.TryGetValue(BlockIds.Key, out var raw) || RawValue.Unwrap(raw) is not { } value)
        {
            return null;
        }

        if (value is string id && RichTextAttributes.CheckBlockId(id) is null)
        {
            return id;
        }

        errors.Add(Invalid($"{path}.{BlockIds.Key}", RichTextAttributes.CheckBlockId(null)!));
        return null;
    }

    private string? BlockName(object? raw, Dictionary<string, object?> map)
    {
        if (map.TryGetValue(Discriminator, out var name))
        {
            return RawValue.Unwrap(name) as string;
        }

        return raw is null ? null : schema.Blocks.FirstOrDefault(block => block.ClrType == raw.GetType())?.Name;
    }

    private JsonObject? ReadComponentItem(SchemaField field, object? raw, string path, List<ValidationError> errors)
    {
        if (schema.FindComponent((string)field.Settings[FieldSettings.Component]) is not { } component)
        {
            errors.Add(Invalid(path, "The component is not part of the schema."));
            return null;
        }

        if (!TryReadMap(raw, out var map))
        {
            errors.Add(Invalid(path, "The value is not an object."));
            return null;
        }

        if (map is null)
        {
            return null;
        }

        ReportUnknown(map, component, path, isBlock: false, errors);
        return ReadObject(component, map, path, errors);
    }

    // Only what was given is kept: a JSON object has no column to fill, and a key left out is the same as no value.
    private JsonObject ReadObject(SchemaType owner, Dictionary<string, object?> map, string path, List<ValidationError> errors)
    {
        JsonObject result = [];
        foreach (var field in owner.Fields)
        {
            if (map.TryGetValue(field.Name, out var raw))
            {
                result[field.Name] = ToNode(field, raw, $"{path}.{field.Name}", errors);
            }
        }

        return result;
    }

    private static void ReportUnknown(Dictionary<string, object?> map, SchemaType owner, string path, bool isBlock, List<ValidationError> errors)
    {
        foreach (var key in map.Keys)
        {
            if (owner.FindField(key) is null && !(isBlock && key is Discriminator or BlockIds.Key))
            {
                errors.Add(Invalid($"{path}.{key}", $"'{key}' is not a field of {owner.Name}."));
            }
        }
    }

    private static object? ReadReference(SchemaField field, object? raw, string path, List<ValidationError> errors)
    {
        var value = RawValue.Unwrap(raw);
        if (value is null)
        {
            return field.Multiple ? Array.Empty<Guid>() : null;
        }

        if (!field.Multiple)
        {
            if (TryReadId(value, out var id))
            {
                return id == Guid.Empty ? null : id;
            }

            errors.Add(Invalid(path, "The value is not an entry id: expected a GUID."));
            return null;
        }

        if (!TryReadReferenceList(value, out var items))
        {
            errors.Add(Invalid(path, "The value is not a list of entry ids."));
            return Array.Empty<Guid>();
        }

        List<Guid> ids = [];
        foreach (var item in items)
        {
            if (TryReadId(RawValue.Unwrap(item), out var id) && id != Guid.Empty)
            {
                ids.Add(id);
            }
            else
            {
                errors.Add(Invalid($"{path}[{ids.Count}]", "The value is not an entry id: expected a GUID."));
            }
        }

        return ids.ToArray();
    }

    private static bool TryReadReferenceList(object value, out IEnumerable<object?> items)
    {
        switch (value)
        {
            case JsonElement { ValueKind: JsonValueKind.Array } array:
                items = [.. array.EnumerateArray().Cast<object?>()];
                return true;
            case string or JsonElement or IEnumerable<KeyValuePair<string, object?>>:
                items = [];
                return false;
            case IEnumerable list:
                items = list.Cast<object?>();
                return true;
            default:
                items = [];
                return false;
        }
    }

    // An id, a Ref<>, a MediaRef, or the reference or populated entry a read gave, so that what was read can be written back.
    public static bool TryReadId(object? value, out Guid id)
    {
        switch (value)
        {
            case Guid guid:
                id = guid;
                return true;
            case string text:
                return Guid.TryParseExact(text, "D", out id);
            case MediaRef media:
                id = media.Id;
                return true;
            case JsonElement { ValueKind: JsonValueKind.Object } element when element.TryGetProperty(IdKey, out var inner):
                return TryReadId(RawValue.Unwrap(inner), out id);
            case IEnumerable<KeyValuePair<string, object?>> map when map.FirstOrDefault(pair => pair.Key == IdKey) is { Key: not null, Value: var inner }:
                return TryReadId(RawValue.Unwrap(inner), out id);
            case not null when value.GetType() is { IsGenericType: true } type && type.GetGenericTypeDefinition() == typeof(Ref<>):
                id = (Guid)type.GetProperty(nameof(Ref<>.Id))!.GetValue(value)!;
                return true;
            default:
                id = Guid.Empty;
                return false;
        }
    }

    // The keys of an object in any form a field type coerces, plus documents and instances of a component or block class.
    public static bool TryReadMap(object? raw, out Dictionary<string, object?>? map)
    {
        map = null;
        switch (raw)
        {
            case null:
                return true;
            case JsonElement or JsonNode or string:
                if (!RawValue.TryReadJson(raw, out var node, out _))
                {
                    return false;
                }

                if (node is JsonObject json)
                {
                    map = json.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.Ordinal);
                }

                return node is null or JsonObject;
            case IEnumerable<KeyValuePair<string, object?>> pairs:
                map = pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                return true;
            case not IEnumerable when Attribute.IsDefined(raw.GetType(), typeof(ComponentAttribute)) || Attribute.IsDefined(raw.GetType(), typeof(BlockAttribute)):
                map = Validation.FieldValue.ReadInstance(raw)!.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                return true;
            default:
                return false;
        }
    }

    private static bool TryReadItems(object? raw, out List<object?>? items)
    {
        items = null;
        switch (raw)
        {
            case null:
                return true;
            case JsonElement or JsonNode or string:
                if (!RawValue.TryReadJson(raw, out var node, out _))
                {
                    return false;
                }

                if (node is JsonArray array)
                {
                    items = [.. array];
                }

                return node is null or JsonArray;
            case IEnumerable<KeyValuePair<string, object?>>:
                return false;
            case IEnumerable list:
                items = [.. list.Cast<object?>()];
                return true;
            default:
                return false;
        }
    }

    private static ValidationError Invalid(string path, string message) => new(path, ValidationErrorCodes.InvalidValue, message);
}
