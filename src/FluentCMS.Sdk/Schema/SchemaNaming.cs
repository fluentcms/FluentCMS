using System.Text.Json;

namespace FluentCMS.Schema;

internal static class SchemaNaming
{
    public static string ToCamelCase(string name) => JsonNamingPolicy.CamelCase.ConvertName(name);

    public static string ToKebabCase(string name) => JsonNamingPolicy.KebabCaseLower.ConvertName(name);

    public static bool IsValidTypeName(string name)
    {
        if (name.Length == 0 || !char.IsAsciiLetterLower(name[0]) || name[^1] == '-')
        {
            return false;
        }

        for (var i = 1; i < name.Length; i++)
        {
            var c = name[i];
            if (c == '-' ? name[i - 1] == '-' : !char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsValidFieldName(string name) =>
        name.Length > 0 && char.IsAsciiLetterLower(name[0]) && name.All(char.IsAsciiLetterOrDigit);
}
