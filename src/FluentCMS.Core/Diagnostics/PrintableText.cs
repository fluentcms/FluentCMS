using System.Globalization;
using System.Text;

namespace FluentCMS.Core.Diagnostics;

// Text that came from elsewhere, such as an instance, a package or a schema, made safe to print: a control character or a
// bidi control is written as its escape (\u001B), so an escape sequence or a line break in a name cannot move the cursor,
// colour the terminal, start a line of its own or reorder what is shown.
internal static class PrintableText
{
    public static bool IsUnsafe(char character) =>
        char.IsControl(character)
        || (int)character is 0x061C or 0x200E or 0x200F or 0x2028 or 0x2029 or (>= 0x202A and <= 0x202E) or (>= 0x2066 and <= 0x2069);

    // With keepLineBreaks, the line breaks and tabs of a message of several lines stay.
    public static string Escape(string? text, bool keepLineBreaks = false)
    {
        if (string.IsNullOrEmpty(text) || !text.Any(character => Escapes(character, keepLineBreaks)))
        {
            return text ?? "";
        }

        var builder = new StringBuilder(text.Length + 16);
        foreach (var character in text)
        {
            if (Escapes(character, keepLineBreaks))
            {
                builder.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static bool Escapes(char character, bool keepLineBreaks) =>
        IsUnsafe(character) && !(keepLineBreaks && character is '\n' or '\r' or '\t');
}
