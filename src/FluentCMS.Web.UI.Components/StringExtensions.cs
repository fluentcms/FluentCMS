namespace FluentCMS;

public static class StringExtensions
{
    // from PascalCase to kebab-case
    public static string FromPascalCaseToKebabCase(this string value)
    {
        return string.Concat(value.Select((x, i) => i > 0 && char.IsUpper(x) ? "-" + x : x.ToString())).ToLower();
    }
}