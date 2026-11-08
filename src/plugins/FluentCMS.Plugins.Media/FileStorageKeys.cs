using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace FluentCMS.Plugins.Media;

// The keys of IFileStorage, for the local disk and the storage packages alike: segments of letters, digits, dots, hyphens and
// underscores joined by slashes, none of dots alone, so that a key is a path on a disk and an object name as it is.
public static partial class FileStorageKeys
{
    public const int MaxLength = 512;

    public static bool IsValid(string? key) =>
        key is { Length: > 0 and <= MaxLength } && key.Split('/').All(segment => Segment().IsMatch(segment) && segment.Any(character => character != '.'));

    public static string Checked(string key, [CallerArgumentExpression(nameof(key))] string? parameter = null) =>
        IsValid(key)
            ? key
            : throw new ArgumentException($"'{key}' is not a storage key: segments of letters, digits, dots, hyphens and underscores joined by slashes.", parameter);

    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,128}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Segment();
}
