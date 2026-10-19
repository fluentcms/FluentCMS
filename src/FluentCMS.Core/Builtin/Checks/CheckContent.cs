using System.Globalization;
using System.Text.Json;
using FluentCMS.Core.Checks;

namespace FluentCMS.Core.Builtin.Checks;

// The places in a subject's fields the built-in checks read, each with a JSON Pointer from the fields' root.
internal static class CheckContent
{
    // Every string, however deep, such as /body, /tags/2 or /price/source/reference.
    public static IEnumerable<(string Pointer, string Text)> Strings(JsonElement fields)
    {
        return Walk(fields, string.Empty).Where(item => item.Value.ValueKind == JsonValueKind.String)
            .Select(item => (item.Pointer, item.Value.GetString()!));
    }

    // Each field's value, or each item of a field that holds a list, as a field of a content type stores it.
    public static IEnumerable<(string Pointer, JsonElement Value)> FieldValues(JsonElement fields)
    {
        if (fields.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var field in fields.EnumerateObject())
        {
            var pointer = Append(string.Empty, field.Name);
            if (field.Value.ValueKind != JsonValueKind.Array)
            {
                yield return (pointer, field.Value);
                continue;
            }

            var index = 0;
            foreach (var item in field.Value.EnumerateArray())
            {
                yield return (Append(pointer, (index++).ToString(CultureInfo.InvariantCulture)), item);
            }
        }
    }

    // The facts of an entity, one per field that holds one (SCH-03); content of any other type has none.
    public static IEnumerable<(string Field, JsonElement Fact)> Facts(CheckSubject subject)
    {
        if (subject.Definition is not { IsEntity: true } || subject.Fields.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        return subject.Fields.EnumerateObject()
            .Where(field => field.Value.ValueKind == JsonValueKind.Object)
            .Select(field => (field.Name, field.Value));
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
                    foreach (var nested in Walk(item, Append(pointer, (index++).ToString(CultureInfo.InvariantCulture))))
                    {
                        yield return nested;
                    }
                }

                break;
        }
    }
}
