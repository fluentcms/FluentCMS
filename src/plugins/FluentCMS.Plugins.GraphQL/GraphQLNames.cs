using System.Text;

namespace FluentCMS.Plugins.GraphQL;

internal static class GraphQLNames
{
    // article-category and articleCategory are both ArticleCategory.
    public static string Pascal(string name)
    {
        var builder = new StringBuilder(name.Length);
        var upper = true;
        foreach (var character in name)
        {
            if (character == '-')
            {
                upper = true;
            }
            else
            {
                builder.Append(upper ? char.ToUpperInvariant(character) : character);
                upper = false;
            }
        }

        return builder.ToString();
    }

    public static string Camel(string name)
    {
        var pascal = Pascal(name);
        return char.ToLowerInvariant(pascal[0]) + pascal[1..];
    }

    // publishedOn and published-on are both PUBLISHED_ON. Null when the text has a character a name cannot hold.
    public static string? EnumValue(string value)
    {
        var builder = new StringBuilder(value.Length + 4);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsAsciiLetterOrDigit(character))
            {
                if (index > 0 && char.IsAsciiLetterUpper(character) && char.IsAsciiLetterLower(value[index - 1]))
                {
                    builder.Append('_');
                }

                builder.Append(char.ToUpperInvariant(character));
            }
            else if (character is '-' or '_' or ' ' or '.')
            {
                builder.Append('_');
            }
            else
            {
                return null;
            }
        }

        if (builder.Length == 0 || builder.ToString() is "TRUE" or "FALSE" or "NULL")
        {
            return null;
        }

        return char.IsAsciiDigit(builder[0]) ? "_" + builder : builder.ToString();
    }
}
