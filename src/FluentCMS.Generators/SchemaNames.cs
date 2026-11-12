using System.Globalization;
using System.Text;

namespace FluentCMS.Generators;

// The names the attribute reader gives: JsonNamingPolicy.CamelCase for a field and JsonNamingPolicy.KebabCaseLower for
// a type, ported because System.Text.Json is not available to a generator on netstandard2.0. A test holds them to the
// policies.
internal static class SchemaNames
{
    public static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name) || !char.IsUpper(name[0]))
        {
            return name;
        }

        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (i == 1 && !char.IsUpper(chars[i]))
            {
                break;
            }

            var hasNext = i + 1 < chars.Length;

            // Stop when the next character is already lower case.
            if (i > 0 && hasNext && !char.IsUpper(chars[i + 1]))
            {
                if (chars[i + 1] == ' ')
                {
                    chars[i] = char.ToLowerInvariant(chars[i]);
                }

                break;
            }

            chars[i] = char.ToLowerInvariant(chars[i]);
        }

        return new string(chars);
    }

    public static string ToKebabCase(string name)
    {
        const char separator = '-';
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var builder = new StringBuilder(name.Length + 8);
        var state = State.NotStarted;
        for (var i = 0; i < name.Length; i++)
        {
            var current = name[i];
            switch (char.GetUnicodeCategory(current))
            {
                case UnicodeCategory.UppercaseLetter:
                    switch (state)
                    {
                        case State.LowercaseLetterOrDigit:
                        case State.SpaceSeparator:
                            builder.Append(separator);
                            break;

                        // In a run of capitals, the last one starts a new word when lower case follows: XMLReader is xml-reader.
                        case State.UppercaseLetter when i + 1 < name.Length && char.IsLower(name[i + 1]):
                            builder.Append(separator);
                            break;
                    }

                    builder.Append(char.ToLowerInvariant(current));
                    state = State.UppercaseLetter;
                    break;

                case UnicodeCategory.LowercaseLetter:
                case UnicodeCategory.DecimalDigitNumber:
                    if (state == State.SpaceSeparator)
                    {
                        builder.Append(separator);
                    }

                    builder.Append(current);
                    state = State.LowercaseLetterOrDigit;
                    break;

                case UnicodeCategory.SpaceSeparator:
                    if (state != State.NotStarted)
                    {
                        state = State.SpaceSeparator;
                    }

                    break;

                default:
                    builder.Append(current);
                    state = State.NotStarted;
                    break;
            }
        }

        return builder.ToString();
    }

    private enum State
    {
        NotStarted,
        UppercaseLetter,
        LowercaseLetterOrDigit,
        SpaceSeparator,
    }
}
