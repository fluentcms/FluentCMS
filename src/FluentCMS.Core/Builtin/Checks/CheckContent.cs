using System.Globalization;
using System.Text.Json;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Builtin.Checks;

// The places in a subject's fields the built-in checks read, each with a JSON Pointer from the fields' root. On an entity a field
// holds a fact, and only the fact's value is content; its source, owner and dates are read by the checks about them (SCH-03).
internal static class CheckContent
{
    // Every string of the content, however deep, such as /body, /tags/2 or, on an entity, /name/value.
    public static IEnumerable<(string Pointer, string Text)> Strings(CheckSubject subject)
    {
        return Roots(subject)
            .SelectMany(root => Walk(root.Value, root.Pointer))
            .Where(item => item.Value.ValueKind == JsonValueKind.String)
            .Select(item => (item.Pointer, item.Value.GetString()!));
    }

    // Every property name inside the content, such as a key of a json field, which a model reads with the content it names.
    public static IEnumerable<(string Pointer, string Name)> Names(CheckSubject subject)
    {
        return Roots(subject)
            .SelectMany(root => Walk(root.Value, root.Pointer))
            .Where(item => item.Value.ValueKind == JsonValueKind.Object)
            .SelectMany(item => item.Value.EnumerateObject().Select(property => (Append(item.Pointer, property.Name), property.Name)));
    }

    // Each field's value, or each item of a field that holds a list, as a field of a content type stores it.
    public static IEnumerable<(string Pointer, JsonElement Value)> FieldValues(CheckSubject subject)
    {
        foreach (var (pointer, value) in Roots(subject))
        {
            if (value.ValueKind != JsonValueKind.Array)
            {
                yield return (pointer, value);
                continue;
            }

            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                yield return (Append(pointer, Index(index++)), item);
            }
        }
    }

    // The facts of an entity, one per field that holds a value (SCH-03); content of any other type has none. A fact that is not an
    // object, as in content written before its type became an entity, has no metadata at all.
    public static IEnumerable<(string Field, JsonElement Fact)> Facts(CheckSubject subject)
    {
        if (subject.Definition is not { IsEntity: true } || subject.Fields.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        return subject.Fields.EnumerateObject()
            .Where(field => field.Value.ValueKind != JsonValueKind.Null)
            .Select(field => (field.Name, field.Value));
    }

    public static JsonElement? Metadata(JsonElement fact, string name)
    {
        return fact.ValueKind == JsonValueKind.Object && fact.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value
            : null;
    }

    // A source names its kind and what it refers to, as entries store it (FactMetadata).
    public static bool HasSource(JsonElement fact)
    {
        return Metadata(fact, FactMetadata.Source) is { ValueKind: JsonValueKind.Object } source
            && source.TryGetProperty(FactMetadata.SourceReference, out var reference)
            && reference.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(reference.GetString());
    }

    // The fields that hold a value and that the change touches.
    public static IReadOnlyList<string> ChangedFields(CheckSubject subject)
    {
        if (subject.Fields.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        return subject.Fields.EnumerateObject()
            .Where(field => field.Value.ValueKind != JsonValueKind.Null)
            .Select(field => field.Name)
            .Where(name => subject.ChangedFields is null || subject.ChangedFields.Contains(name, StringComparer.Ordinal))
            .ToList();
    }

    public static string Append(string pointer, string token)
    {
        return $"{pointer}/{token.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal)}";
    }

    // The field a pointer starts in, for messages: "/gallery/0/alt" is in "gallery".
    public static string FieldOf(string pointer)
    {
        var token = pointer.Length > 1 ? pointer[1..].Split('/')[0] : pointer;
        return token.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
    }

    // A quote of found text, kept short so a finding stays readable.
    public static string Quote(string text, int max = 120)
    {
        var line = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= max ? line : string.Concat(line.AsSpan(0, max - 1), "…");
    }

    public static bool HasValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Undefined or JsonValueKind.Null => false,
            JsonValueKind.String => !string.IsNullOrWhiteSpace(value.GetString()),
            JsonValueKind.Array => value.GetArrayLength() > 0,
            JsonValueKind.Object => value.EnumerateObject().Any(),
            _ => true,
        };
    }

    // Each field's content: its value, or on an entity the value inside its fact.
    private static IEnumerable<(string Pointer, JsonElement Value)> Roots(CheckSubject subject)
    {
        if (subject.Fields.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        var entity = subject.Definition is { IsEntity: true };
        foreach (var field in subject.Fields.EnumerateObject())
        {
            var pointer = Append(string.Empty, field.Name);
            if (!entity)
            {
                yield return (pointer, field.Value);
            }
            else if (field.Value.ValueKind == JsonValueKind.Object && field.Value.TryGetProperty(FactMetadata.Value, out var value))
            {
                yield return (Append(pointer, FactMetadata.Value), value);
            }
            else if (field.Value.ValueKind != JsonValueKind.Object)
            {
                yield return (pointer, field.Value);
            }
        }
    }

    private static IEnumerable<(string Pointer, JsonElement Value)> Walk(JsonElement value, string pointer)
    {
        yield return (pointer, value);

        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in value.EnumerateObject())
                {
                    foreach (var nested in Walk(property.Value, Append(pointer, property.Name)))
                    {
                        yield return nested;
                    }
                }

                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in value.EnumerateArray())
                {
                    foreach (var nested in Walk(item, Append(pointer, Index(index++))))
                    {
                        yield return nested;
                    }
                }

                break;
        }
    }

    private static string Index(int index)
    {
        return index.ToString(CultureInfo.InvariantCulture);
    }
}
