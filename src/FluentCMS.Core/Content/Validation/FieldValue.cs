using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.ProseMirror;
using FluentCMS.Media;
using FluentCMS.Schema;

namespace FluentCMS.Core.Content.Validation;

// How validation reads a value: whether it is empty, what it compares as, and how a component or block instance
// is read, either a dictionary keyed by API field name or a CLR object whose public properties are the fields.
internal static class FieldValue
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Properties = new();

    public static bool IsEmpty(object? value) => value switch
    {
        null => true,
        string text => string.IsNullOrWhiteSpace(text),
        Guid id => id == Guid.Empty,
        MediaRef media => media.IsEmpty,
        RichText document => IsEmptyDocument(document),
        IEnumerable items => !items.GetEnumerator().MoveNext(),
        _ => IsEmptyRef(value),
    };

    // The value as a schema stores it: null, string, bool, long or decimal. An enum member is its camelCase
    // name, a reference is its id. Anything else cannot be compared with a condition value.
    public static object? Normalize(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (IsRef(value.GetType()))
        {
            return value.GetType().GetProperty("Id")!.GetValue(value)?.ToString();
        }

        try
        {
            return SchemaValue.Normalize(value);
        }
        catch (ArgumentException)
        {
            return value;
        }
    }

    public static IEnumerable<object?>? Items(object? value) =>
        value is IEnumerable and not string and not IReadOnlyDictionary<string, object?> ? ((IEnumerable)value).Cast<object?>() : null;

    public static IReadOnlyDictionary<string, object?>? ReadInstance(object? instance)
    {
        switch (instance)
        {
            case null:
                return null;
            case IReadOnlyDictionary<string, object?> dictionary:
                return dictionary;
            case IDictionary<string, object?> mutable:
                return new Dictionary<string, object?>(mutable, StringComparer.Ordinal);
        }

        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in Properties.GetOrAdd(instance.GetType(), ReadableProperties))
        {
            values[SchemaNaming.ToCamelCase(property.Name)] = property.GetValue(instance);
        }

        return values;
    }

    // What a draft holds for no rich text, and what an editor leaves behind: blocks with no text, and nothing a reader
    // would see without any, such as an image or a rule. A document that cannot be read is for its own rule to refuse.
    private static bool IsEmptyDocument(RichText document)
    {
        try
        {
            return HasNoContent(JsonDepth.Parse(document.Json));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasNoContent(JsonNode? node)
    {
        if (node is not JsonObject item || item["type"] is not JsonValue kind || !kind.TryGetValue<string>(out var type))
        {
            return false;
        }

        if (type == RichTextNodeTypes.Text)
        {
            return item["text"] is not JsonValue value || !value.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text);
        }

        if (type is not (RichTextNodeTypes.Doc or RichTextNodeTypes.Paragraph or RichTextNodeTypes.Heading or RichTextNodeTypes.BulletList
            or RichTextNodeTypes.OrderedList or RichTextNodeTypes.ListItem or RichTextNodeTypes.Blockquote or RichTextNodeTypes.CodeBlock or RichTextNodeTypes.HardBreak))
        {
            return false;
        }

        return item["content"] is not JsonArray content || content.All(HasNoContent);
    }

    private static PropertyInfo[] ReadableProperties(Type type) =>
        [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(property => property.GetIndexParameters().Length == 0 && property.CanRead)];

    private static bool IsRef(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Ref<>);

    private static bool IsEmptyRef(object value) =>
        IsRef(value.GetType()) && (bool)value.GetType().GetProperty("IsEmpty")!.GetValue(value)!;
}
