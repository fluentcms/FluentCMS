using System.Collections.Frozen;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Schema;

namespace FluentCMS.Core.Jobs;

// An exception message can quote the job's input, so every string, number and property name of the payload is taken
// out of what is stored and logged, except the values that are names of the schema's types and fields, which are not
// secret and which an error such as "Content type 'article' not found" is no use without. This cannot catch a secret the
// job has encoded or derived.
internal static class JobErrorText
{
    private const string Redacted = "***";
    private const string Truncated = "...";

    // A shorter value is matched only as a whole word, since inside a word it is more likely chance than a quote.
    private const int AnywhereFromLength = 4;

    public static string ForStorage(Exception exception, string payload, IReadOnlySet<string>? kept = null)
    {
        if (exception is JobInvocationException)
        {
            return Cap(exception.Message);
        }

        var values = ValuesOf(payload, kept);
        var text = new StringBuilder();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            text.Append(text.Length == 0 ? "" : " ---> ").Append(current.GetType().FullName).Append(": ").Append(Scrub(current.Message, values));
        }

        return Cap(text.ToString());
    }

    public static string ForLog(Exception exception, string payload, IReadOnlySet<string>? kept = null) => Scrub(exception.ToString(), ValuesOf(payload, kept));

    // The names of the content types, components and blocks of the schema, and of their fields.
    public static FrozenSet<string> NamesOf(SchemaModel schema) =>
        schema.ContentTypes.Concat(schema.Components).Concat(schema.Blocks)
            .SelectMany(type => type.Fields.Select(field => field.Name).Prepend(type.Name))
            .ToFrozenSet(StringComparer.Ordinal);

    public static string Cap(string text)
    {
        if (text.Length <= Job.LastErrorMaxLength)
        {
            return text;
        }

        var keep = Job.LastErrorMaxLength - Truncated.Length;
        if (char.IsHighSurrogate(text[keep - 1]))
        {
            keep--;
        }

        return string.Concat(text.AsSpan(0, keep), Truncated);
    }

    private static string Scrub(string text, List<string> values)
    {
        foreach (var value in values)
        {
            text = value.Length >= AnywhereFromLength
                ? text.Replace(value, Redacted, StringComparison.Ordinal)
                : Regex.Replace(text, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(value)}(?![\p{{L}}\p{{N}}])", Redacted, RegexOptions.CultureInvariant);
        }

        return text;
    }

    // The longest first, so that a value inside another does not leave the rest of it behind.
    private static List<string> ValuesOf(string payload, IReadOnlySet<string>? kept)
    {
        var values = new List<string>();
        try
        {
            using var document = JsonDocument.Parse(payload);
            Collect(document.RootElement, values);
        }
        catch (JsonException)
        {
            values.Add(payload);
        }

        return [.. values.Where(value => value.Length > 0 && kept?.Contains(value) != true).Distinct(StringComparer.Ordinal).OrderByDescending(value => value.Length)];
    }

    private static void Collect(JsonElement element, List<string> values)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                values.Add(element.GetString()!);
                break;
            case JsonValueKind.Number:
                values.Add(element.GetRawText());
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Collect(item, values);
                }

                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    values.Add(property.Name);
                    Collect(property.Value, values);
                }

                break;
        }
    }
}
