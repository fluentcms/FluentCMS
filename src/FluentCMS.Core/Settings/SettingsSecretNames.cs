using System.Text.RegularExpressions;

namespace FluentCMS.Core.Settings;

// Which names of properties are secret without an attribute. It is stricter than the scrubber of events and logs, which has to
// be broad because it reads free text: a property called Keywords, SortKey or Tokenizer is not a secret, and hiding it would
// only make the form of the admin wrong. A name is read as words (camelCase, PascalCase, snake_case, an acronym is one word),
// and is a secret when its last word is password, passphrase, secret or token; when its last two words are api key, private
// key, secret key, access key, client secret or connection string.
internal static partial class SettingsSecretNames
{
    private static readonly string[] LastWord = ["password", "passphrase", "secret", "token"];

    private static readonly string[] LastTwoWords = ["api key", "private key", "secret key", "access key", "client secret", "connection string"];

    [GeneratedRegex("[A-Z]+(?![a-z])|[A-Z]?[a-z]+|[0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex Word();

    public static bool Matches(string name)
    {
        var words = Word().Matches(name).Select(match => match.Value.ToLowerInvariant()).ToArray();
        if (words.Length == 0)
        {
            return false;
        }

        return LastWord.Contains(words[^1], StringComparer.Ordinal)
            || (words.Length >= 2 && LastTwoWords.Contains(words[^2] + " " + words[^1], StringComparer.Ordinal));
    }
}
