using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Content.Hooks;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Media;
using FluentCMS.Publishing;
using FluentCMS.Schema;

namespace FluentCMS.Core.Content;

// Maps a content class to and from ContentDocument by reflection, until the source generator of M6 takes over. A
// document that goes back to the service holds plain ids, documents and lists, which RejectUnwritable can read inside.
// A value that does not fit a property leaves it at its default: validation reports it from the document, not from here.
internal sealed class ContentEntryMapper(SchemaModel schema) : IHookEntryMapper
{
    private const string Discriminator = "type";

    private readonly ConcurrentDictionary<Type, ClassMap> _classes = new();

    public object ToEntry(Type clrType, ContentDocument document)
    {
        ArgumentNullException.ThrowIfNull(clrType);
        ArgumentNullException.ThrowIfNull(document);

        return ToInstance(clrType, document);
    }

    // Writes only what a hook changed: a property that still holds what the document gave it says nothing, even where
    // the class cannot tell an empty text from no value.
    public void CopyTo(object entry, ContentDocument document)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(document);

        var empty = Activator.CreateInstance(entry.GetType());
        foreach (var (field, property) in MapOf(entry.GetType()).Fields)
        {
            document.TryGetValue(field.Name, out var stored);
            var before = stored is not null && TryConvert(stored, property.PropertyType, out var converted) && converted is not null
                ? converted
                : property.GetValue(empty);
            var after = property.GetValue(entry);

            if (!EntryJson.Same(ToApi(before), ToApi(after)))
            {
                document[field.Name] = ToApi(after);
            }
        }
    }

    // The document of a typed write: a property with no value is not named, because a class cannot leave a field out.
    // Nor is one that include turns down, at the top level. A hidden property holds nothing a read gave it, and a
    // read-only one is not the client's to send, at any level. The system principal sends both.
    public ContentDocument ToDocument(object entry, Func<SchemaField, bool> include, bool isSystem)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(include);

        bool Inside(SchemaField field) => isSystem || (!field.Hidden && !field.ReadOnly);
        return ToFields(entry, field => Inside(field) && include(field), Inside);
    }

    // What validation reads: nested objects as documents and each value as the property of its class declares it, where
    // it fits, so that a custom validator of the class gets its own types. What does not fit stays, for the rules to report.
    public IReadOnlyDictionary<string, object?> ValidationValues(SchemaType type, ContentDocument entry) => View(type, entry);

    private ContentDocument View(SchemaType owner, IEnumerable<KeyValuePair<string, object?>> values)
    {
        ContentDocument view = [];
        foreach (var (key, value) in values)
        {
            if (owner.FindField(key) is { } field)
            {
                view[key] = ViewValue(owner, field, value);
            }
            else if (key == Discriminator)
            {
                view[key] = RawValue.Unwrap(value);
            }
        }

        return view;
    }

    private object? ViewValue(SchemaType owner, SchemaField field, object? value)
    {
        switch (field.FieldType)
        {
            case FieldTypeNames.Component when schema.FindComponent((string)field.Settings[FieldSettings.Component]) is { } component:
                return field.Multiple
                    ? ItemsOf(value).Select(item => (object?)View(component, Pairs(item))).ToList()
                    : value is null ? null : View(component, Pairs(value));
            case FieldTypeNames.Blocks:
                return ItemsOf(value).Select(item => (object?)ViewBlock(item)).ToList();
        }

        var plain = schema.FieldTypes.Find(field.FieldType)!.StorageKind(field) == FieldStorageKind.Reference ? PlainReference(field, value) : Plain(field, value);
        return owner.ClrType is { } clrType
            && MapOf(clrType).Fields.FirstOrDefault(item => item.Field == field).Property is { } property
            && TryConvert(plain, property.PropertyType, out var typed) && typed is not null
                ? typed
                : plain;
    }

    private ContentDocument ViewBlock(object? item)
    {
        var pairs = Pairs(item);
        var name = pairs.FirstOrDefault(pair => pair.Key == Discriminator).Value;
        return RawValue.Unwrap(name) is string blockName && schema.FindBlock(blockName) is { } block ? View(block, pairs) : new ContentDocument(pairs);
    }

    private static object? PlainReference(SchemaField field, object? value)
    {
        if (!field.Multiple)
        {
            return value is Guid ? value : EntryCanonicalizer.TryReadId(RawValue.Unwrap(value), out var id) && id != Guid.Empty ? id : null;
        }

        return ItemsOf(value).Select(item => EntryCanonicalizer.TryReadId(item is Guid ? item : RawValue.Unwrap(item), out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).ToArray();
    }

    private static object? Plain(SchemaField field, object? value)
    {
        if (field.FieldType == FieldTypeNames.RichText && value is JsonObject document)
        {
            return new RichText(EntryJson.Text(document));
        }

        return value switch
        {
            JsonArray items when field.FieldType != FieldTypeNames.Json => items.Select(RawValue.Unwrap).ToList(),
            JsonValue => RawValue.Unwrap(value),
            _ => value,
        };
    }

    private object ToInstance(Type clrType, IEnumerable<KeyValuePair<string, object?>> values)
    {
        var instance = Activator.CreateInstance(clrType)
            ?? throw new InvalidOperationException($"{clrType} cannot be created.");

        var known = values as ContentDocument ?? new ContentDocument(values);
        if (instance is ContentItem item)
        {
            ReadSystemFields(item, known);
        }

        foreach (var (field, property) in MapOf(clrType).Fields)
        {
            if (property.CanWrite && known.TryGetValue(field.Name, out var value) && value is not null
                && TryConvert(value, property.PropertyType, out var converted) && converted is not null)
            {
                property.SetValue(instance, converted);
            }
        }

        return instance;
    }

    private static void ReadSystemFields(ContentItem item, ContentDocument values)
    {
        if (values.TryGetValue("id", out var id) && id is Guid entry)
        {
            item.Id = entry;
        }

        if (values.TryGetValue("status", out var status) && status is EntryStatus state)
        {
            item.Status = state;
        }

        if (values.TryGetValue("locale", out var locale) && locale is string tag)
        {
            item.Locale = tag;
        }

        if (values.TryGetValue("version", out var version) && version is int number)
        {
            item.Version = number;
        }

        if (values.TryGetValue("createdAt", out var createdAt) && createdAt is DateTimeOffset created)
        {
            item.CreatedAt = created;
        }

        if (values.TryGetValue("createdBy", out var createdBy) && createdBy is Guid creator)
        {
            item.CreatedBy = creator;
        }

        if (values.TryGetValue("updatedAt", out var updatedAt) && updatedAt is DateTimeOffset updated)
        {
            item.UpdatedAt = updated;
        }

        if (values.TryGetValue("updatedBy", out var updatedBy) && updatedBy is Guid updater)
        {
            item.UpdatedBy = updater;
        }

        if (values.TryGetValue("publishedAt", out var publishedAt) && publishedAt is DateTimeOffset published)
        {
            item.PublishedAt = published;
        }
    }

    private bool TryConvert(object? value, Type declared, out object? result)
    {
        result = null;
        if (value is null)
        {
            return true;
        }

        var target = Nullable.GetUnderlyingType(declared) ?? declared;
        if (target.IsInstanceOfType(value))
        {
            result = value;
            return true;
        }

        if (target.IsGenericType && target.GetGenericTypeDefinition() == typeof(Ref<>))
        {
            return TryReadRef(value, target, out result);
        }

        if (target == typeof(MediaRef))
        {
            if (EntryCanonicalizer.TryReadId(value is Guid ? value : RawValue.Unwrap(value), out var id))
            {
                result = new MediaRef(id);
                return true;
            }

            return false;
        }

        if (target == typeof(RichText) && value is JsonObject document)
        {
            result = new RichText(EntryJson.Text(document));
            return true;
        }

        if (target == typeof(JsonElement) || target == typeof(JsonNode))
        {
            return TryConvertJson(value, target, out result);
        }

        var raw = RawValue.Unwrap(value);
        if (raw is null)
        {
            return true;
        }

        if (target == typeof(BlockList))
        {
            return TryBuildBlockList(value, out result);
        }

        if (Attribute.IsDefined(target, typeof(ComponentAttribute)) || Attribute.IsDefined(target, typeof(BlockAttribute)))
        {
            if (Pairs(value) is { Count: > 0 } pairs)
            {
                result = ToInstance(target, pairs);
                return true;
            }

            return false;
        }

        if (ListItemType(target) is { } itemType)
        {
            return TryBuildList(value, target, itemType, out result);
        }

        return TryConvertScalar(raw, target, out result);
    }

    private static bool TryReadRef(object value, Type target, out object? result)
    {
        result = null;
        if (!EntryCanonicalizer.TryReadId(value is Guid ? value : RawValue.Unwrap(value), out var id))
        {
            return false;
        }

        result = Activator.CreateInstance(target, id);
        return true;
    }

    private static bool TryConvertJson(object value, Type target, out object? result)
    {
        result = null;
        var node = EntryJson.ToNode(value);
        if (node is null)
        {
            return true;
        }

        result = target == typeof(JsonNode) ? node : JsonSerializer.SerializeToElement(node);
        return true;
    }

    private bool TryBuildBlockList(object value, out object? result)
    {
        List<object> blocks = [];
        foreach (var item in ItemsOf(value))
        {
            var pairs = Pairs(item);
            var name = RawValue.Unwrap(pairs.FirstOrDefault(pair => pair.Key == Discriminator).Value) as string;
            if (name is not null && schema.FindBlock(name)?.ClrType is { } clrType)
            {
                blocks.Add(ToInstance(clrType, pairs));
            }
            else if (item is not null)
            {
                blocks.Add(item);
            }
        }

        result = new BlockList(blocks);
        return true;
    }

    private bool TryBuildList(object value, Type target, Type itemType, out object? result)
    {
        result = null;
        List<object?> items = [];
        foreach (var item in ItemsOf(value))
        {
            if (!TryConvert(item, itemType, out var converted))
            {
                return false;
            }

            items.Add(converted);
        }

        if (target == typeof(MediaRefList))
        {
            result = new MediaRefList(items.Cast<MediaRef>());
            return true;
        }

        var array = Array.CreateInstance(itemType, items.Count);
        for (var index = 0; index < items.Count; index++)
        {
            array.SetValue(items[index], index);
        }

        if (target.IsAssignableFrom(array.GetType()))
        {
            result = array;
            return true;
        }

        // A RefList<T> or a List<T>: each has a constructor that takes the items.
        result = Activator.CreateInstance(target, array);
        return result is not null;
    }

    private static Type? ListItemType(Type target)
    {
        if (target == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(target) && !target.IsInterface)
        {
            return null;
        }

        if (target.IsArray)
        {
            return target.GetElementType();
        }

        return new[] { target }.Concat(target.GetInterfaces())
            .FirstOrDefault(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];
    }

    private static bool TryConvertScalar(object raw, Type target, out object? result)
    {
        result = null;
        try
        {
            switch (raw)
            {
                case var _ when target.IsInstanceOfType(raw):
                    result = raw;
                    return true;
                case string text when target.IsEnum:
                    if (text.Length > 0 && !char.IsAsciiDigit(text[0]) && Enum.TryParse(target, text, ignoreCase: true, out var member))
                    {
                        result = member;
                        return true;
                    }

                    return false;
                case string text when target == typeof(DateOnly):
                    if (DateOnly.TryParse(text, CultureInfo.InvariantCulture, out var day))
                    {
                        result = day;
                        return true;
                    }

                    return false;
                case string text when target == typeof(DateTimeOffset):
                    if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var moment))
                    {
                        result = moment;
                        return true;
                    }

                    return false;
                case not string when RawValue.IsNumber(raw) && Type.GetTypeCode(target) is not (TypeCode.Object or TypeCode.String or TypeCode.Boolean or TypeCode.DateTime):
                    result = Convert.ChangeType(raw, target, CultureInfo.InvariantCulture);
                    return true;
                default:
                    return false;
            }
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private ContentDocument ToFields(object instance, Func<SchemaField, bool>? include, Func<SchemaField, bool>? inside)
    {
        ContentDocument fields = [];
        var type = instance.GetType();
        if (schema.Blocks.FirstOrDefault(block => block.ClrType == type) is { } named)
        {
            fields[Discriminator] = named.Name;
        }

        foreach (var (field, property) in MapOf(type).Fields)
        {
            if (include?.Invoke(field) == false)
            {
                continue;
            }

            var value = ToApi(property.GetValue(instance), inside);
            if (!IsEmpty(value))
            {
                fields[field.Name] = value;
            }
        }

        return fields;
    }

    private static bool IsEmpty(object? value) => value switch
    {
        null or "" => true,
        RichText document => document == RichText.Empty,
        ContentDocument group => group.Values.All(IsEmpty),
        ICollection list => list.Count == 0,
        _ => false,
    };

    private object? ToApi(object? value, Func<SchemaField, bool>? inside = null)
    {
        switch (value)
        {
            case null or string or bool or JsonNode or RichText or DateOnly or DateTimeOffset or JsonElement or Guid:
                return value;
            case Enum member:
                return SchemaNaming.ToCamelCase(member.ToString());
            case MediaRef media:
                return media.IsEmpty ? null : media.Id;
            case MediaRefList medias:
                return medias.Where(media => !media.IsEmpty).Select(media => media.Id).ToArray();
            case BlockList blocks:
                return blocks.Select(block => (object?)ToFields(block, inside, inside)).ToList();
            case not null when RawValue.IsNumber(value):
                return value;
        }

        var type = value!.GetType();
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Ref<>))
        {
            return EntryCanonicalizer.TryReadId(value, out var id) && id != Guid.Empty ? id : null;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(RefList<>))
        {
            return ((IEnumerable)value).Cast<object?>().Select(item => EntryCanonicalizer.TryReadId(item, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).ToArray();
        }

        if (Attribute.IsDefined(type, typeof(ComponentAttribute)) || Attribute.IsDefined(type, typeof(BlockAttribute)))
        {
            return ToFields(value, inside, inside);
        }

        return value is IEnumerable items and not IEnumerable<KeyValuePair<string, object?>> ? items.Cast<object?>().Select(item => ToApi(item, inside)).ToList() : value;
    }

    private static List<object?> ItemsOf(object? value) => value switch
    {
        null => [],
        JsonArray array => [.. array],
        string or IEnumerable<KeyValuePair<string, object?>> => [],
        IEnumerable items => [.. items.Cast<object?>()],
        _ => [],
    };

    private static List<KeyValuePair<string, object?>> Pairs(object? value) =>
        EntryCanonicalizer.TryReadMap(value, out var map) && map is not null ? [.. map] : [];

    private ClassMap MapOf(Type clrType) => _classes.GetOrAdd(clrType, type =>
    {
        var described = schema.FindContentType(type)
            ?? schema.Components.Concat(schema.Blocks).FirstOrDefault(item => item.ClrType == type);
        if (described is null)
        {
            return new ClassMap([]);
        }

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetIndexParameters().Length == 0)
            .ToDictionary(property => SchemaNaming.ToCamelCase(property.Name), StringComparer.Ordinal);

        return new ClassMap([.. described.Fields
            .Where(field => properties.ContainsKey(field.Name))
            .Select(field => (field, properties[field.Name]))]);
    });

    private sealed record ClassMap(IReadOnlyList<(SchemaField Field, PropertyInfo Property)> Fields);
}
