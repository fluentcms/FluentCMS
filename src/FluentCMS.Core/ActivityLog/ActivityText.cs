using System.Text.Json;
using FluentCMS.Activity;
using FluentCMS.Core.Events;

namespace FluentCMS.Core.ActivityLog;

// What may be stored of an entry. The log is read by people who cannot necessarily read the content, so the text of an
// entry is scrubbed as the text of an error is, a detail named like a secret loses its value, and everything is cut to the
// length of its column.
internal static class ActivityText
{
    public const int ActionMaxLength = 64;
    public const int TypeMaxLength = 64;
    public const int IdMaxLength = 128;
    public const int LabelMaxLength = 256;
    public const int LocaleMaxLength = 16;
    public const int IpMaxLength = 45;
    public const int ActorNameMaxLength = 128;
    public const int ClientIdMaxLength = 128;
    public const int CorrelationIdMaxLength = 128;

    private const int DetailNameMaxLength = 64;
    private const int DetailValueMaxLength = 256;
    private const int DetailsMaxLength = 2000;

    public static string Action(string action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        var lower = action.ToLowerInvariant();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(lower.Length, ActionMaxLength, nameof(action));
        return lower;
    }

    public static string? Of(string? text, int maxLength) =>
        string.IsNullOrEmpty(text) ? null : Cut(SecretScrubber.Of(text), maxLength);

    // The text of a column that cannot be null.
    public static string Required(string? text, int maxLength) => Cut(SecretScrubber.Of(text ?? ""), maxLength);

    public static string? Details(IReadOnlyDictionary<string, string?>? details)
    {
        if (details is not { Count: > 0 })
        {
            return null;
        }

        var kept = new SortedDictionary<string, string?>(StringComparer.Ordinal);
        var length = 2;
        foreach (var (name, value) in details)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var key = Cut(name, DetailNameMaxLength);
            var text = SecretScrubber.NamesSecret(name) ? "***" : value is null ? null : Cut(SecretScrubber.Of(value), DetailValueMaxLength);
            length += key.Length + (text?.Length ?? 4) + 6;
            if (length > DetailsMaxLength)
            {
                break;
            }

            kept[key] = text;
        }

        return kept.Count == 0 ? null : JsonSerializer.Serialize(kept);
    }

    public static IReadOnlyDictionary<string, string?> ReadDetails(string? json) =>
        string.IsNullOrEmpty(json)
            ? new Dictionary<string, string?>()
            : JsonSerializer.Deserialize<Dictionary<string, string?>>(json) ?? [];

    // Not through the middle of a surrogate pair, which would leave a character that cannot be encoded.
    private static string Cut(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        var end = char.IsHighSurrogate(text[maxLength - 1]) ? maxLength - 1 : maxLength;
        return text[..end];
    }
}
