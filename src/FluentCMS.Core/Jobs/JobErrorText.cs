using System.Text;
using System.Text.Json;
using FluentCMS.Core.Storage.Entities;

namespace FluentCMS.Core.Jobs;

// What may be stored and logged about a failure. An exception message can quote the job's input, so every string of
// the payload is taken out of it. This cannot catch a secret the job has encoded or derived.
internal static class JobErrorText
{
    private const string Redacted = "***";
    private const string Truncated = "...";

    // A shorter value is not a secret, and removing it would garble the message.
    private const int MinScrubbedLength = 4;

    // For last_error and cms_job_attempt.error: the types and messages, without a stack trace.
    public static string ForStorage(Exception exception, string payload)
    {
        if (exception is JobInvocationException)
        {
            return Cap(exception.Message);
        }

        var text = new StringBuilder();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            text.Append(text.Length == 0 ? "" : " ---> ").Append(current.GetType().FullName).Append(": ").Append(current.Message);
        }

        return Cap(Scrub(text.ToString(), payload));
    }

    public static string ForLog(Exception exception, string payload) => Scrub(exception.ToString(), payload);

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

    private static string Scrub(string text, string payload)
    {
        foreach (var value in Strings(payload).Where(value => value.Length >= MinScrubbedLength).Distinct(StringComparer.Ordinal).OrderByDescending(value => value.Length))
        {
            text = text.Replace(value, Redacted, StringComparison.Ordinal);
        }

        return text;
    }

    private static List<string> Strings(string payload)
    {
        var strings = new List<string>();
        try
        {
            using var document = JsonDocument.Parse(payload);
            Collect(document.RootElement, strings);
        }
        catch (JsonException)
        {
            strings.Add(payload);
        }

        return strings;
    }

    private static void Collect(JsonElement element, List<string> strings)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                strings.Add(element.GetString()!);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Collect(item, strings);
                }

                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Collect(property.Value, strings);
                }

                break;
        }
    }
}
