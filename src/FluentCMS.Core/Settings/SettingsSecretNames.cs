using System.Text.RegularExpressions;

namespace FluentCMS.Core.Settings;

// Which names of properties are secret without an attribute. This is a safety net for the author who forgets the attribute
// [SettingsSecret], which is the real control. It is stricter than the scrubber of events and logs, which has to be broad because
// it reads free text: a property called Keywords, SortKey or Tokenizer is not a secret, and hiding it would only make the form of
// the admin wrong.
//
// A name is read as words (camelCase, PascalCase, snake_case; an acronym or an all-capital word is one word), after the digits
// and a version marker at its end are dropped (token2, passwordV2). It is a secret when its last word is password, passphrase,
// secret, token, pwd or apikey, or when its last two words are api, private, secret, access, signing or encryption followed by
// key, or client followed by secret, or connection followed by string. A plural counts too.
//
// The rule is for a property whose value can be text or key material, which a secret is: a Boolean, an enum, a number of up to
// 64 bits, a date, a time or a duration never is, alone, nullable or as the items of a list, so MaxOutputTokens is not a
// secret. A dictionary always can be, since its keys are text, and so can bytes as the items of any list or array, a Guid,
// an Int128, a UInt128, a decimal, an object, and a type that enumerates itself.
internal static partial class SettingsSecretNames
{
    private static readonly HashSet<string> LastWord = WithPlurals("password", "passphrase", "secret", "token", "pwd", "apikey");

    private static readonly HashSet<string> LastTwoWords = Pairs(
        ["api", "private", "secret", "access", "signing", "encryption"], ["key", "keys"],
        ["client"], ["secret", "secrets"],
        ["connection"], ["string", "strings"]);

    private static readonly HashSet<Type> NeverText =
    [
        typeof(bool), typeof(byte), typeof(sbyte), typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong),
        typeof(nint), typeof(nuint), typeof(Half), typeof(float), typeof(double),
        typeof(DateTime), typeof(DateTimeOffset), typeof(DateOnly), typeof(TimeOnly), typeof(TimeSpan),
    ];

    [GeneratedRegex("[A-Z]+(?![a-z])|[A-Z]?[a-z]+|[0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex Word();

    public static bool AppliesTo(Type type)
    {
        HashSet<Type> seen = [];
        var isItem = false;
        while (seen.Add(type))
        {
            type = Nullable.GetUnderlyingType(type) ?? type;

            // Bytes in a list may be a key, whether JSON holds them as base64 or as numbers.
            if (isItem && (type == typeof(byte) || type == typeof(sbyte)))
            {
                return true;
            }

            if (type.IsEnum || NeverText.Contains(type))
            {
                return false;
            }

            // Not a list: a string, a Guid, an object, or the pair of a dictionary, whose key is text.
            if (ItemTypeOf(type) is not { } item)
            {
                return true;
            }

            type = item;
            isItem = true;
        }

        return true;
    }

    public static bool Matches(string name)
    {
        var words = Word().Matches(name).Select(match => match.Value.ToLowerInvariant()).ToList();

        var versioned = false;
        while (words.Count > 0 && words[^1].All(char.IsAsciiDigit))
        {
            words.RemoveAt(words.Count - 1);
            versioned = true;
        }

        if (versioned && words.Count > 0 && words[^1] == "v")
        {
            words.RemoveAt(words.Count - 1);
        }

        if (words.Count == 0)
        {
            return false;
        }

        return LastWord.Contains(words[^1])
            || (words.Count >= 2 && LastTwoWords.Contains(words[^2] + " " + words[^1]));
    }

    private static Type? ItemTypeOf(Type type)
    {
        if (type == typeof(string))
        {
            return null;
        }

        if (type.IsArray)
        {
            return type.GetElementType();
        }

        var interfaces = type.IsInterface ? type.GetInterfaces().Append(type) : type.GetInterfaces();
        return interfaces.FirstOrDefault(item => item.IsGenericType && item.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0];
    }

    private static HashSet<string> WithPlurals(params string[] words) =>
        [.. words, .. words.Select(word => word + "s")];

    private static HashSet<string> Pairs(params string[][] groups)
    {
        HashSet<string> pairs = [];
        for (var index = 0; index < groups.Length; index += 2)
        {
            foreach (var first in groups[index])
            {
                foreach (var second in groups[index + 1])
                {
                    pairs.Add(first + " " + second);
                }
            }
        }

        return pairs;
    }
}
