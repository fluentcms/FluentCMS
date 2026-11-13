using System.Text;

namespace FluentCMS.Core.Storage;

// A LIKE pattern that finds a text anywhere, read as it is. Every character a provider's LIKE gives a meaning is escaped:
// % and _, [ (SQL Server alone, but an escaped character matches itself on every provider) and the escape itself.
public static class LikePattern
{
    public const string Escape = "\\";

    public static string Containing(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var pattern = new StringBuilder(text.Length + 2).Append('%');
        foreach (var character in text)
        {
            if (character is '%' or '_' or '[' or '\\')
            {
                pattern.Append('\\');
            }

            pattern.Append(character);
        }

        return pattern.Append('%').ToString();
    }
}
