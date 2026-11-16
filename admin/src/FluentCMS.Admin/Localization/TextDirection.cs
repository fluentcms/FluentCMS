using System.Globalization;

namespace FluentCMS.Admin.Localization;

// The direction of a language tag from its script, not from TextInfo.IsRightToLeft: the culture data of WebAssembly lacks most
// right-to-left locales and answers left to right for them, such as ur, ps or yi.
internal static class TextDirection
{
    public const string LeftToRight = "ltr";
    public const string RightToLeft = "rtl";

    private static readonly HashSet<string> RightToLeftScripts = new(StringComparer.OrdinalIgnoreCase)
    {
        "Adlm", "Arab", "Hebr", "Nkoo", "Rohg", "Syrc", "Thaa",
    };

    // Languages whose default script is one of the above, as CLDR has them.
    private static readonly HashSet<string> RightToLeftLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "ar", "arc", "bal", "bqi", "ckb", "dv", "fa", "glk", "he", "iw", "ji", "ks", "lrc", "mzn", "nqo", "pnb", "prs", "ps",
        "rhg", "sd", "sdh", "skr", "syr", "ug", "ur", "yi",
    };

    public static string Of(CultureInfo culture) => Of(culture.Name);

    // An explicit script decides (ar-Latn is left to right, az-Arab right to left); otherwise the language.
    public static string Of(string languageTag)
    {
        var subtags = languageTag.Split('-', '_');
        var script = subtags.Skip(1).FirstOrDefault(subtag => subtag.Length == 4 && subtag.All(char.IsAsciiLetter));
        var rightToLeft = script is not null ? RightToLeftScripts.Contains(script) : RightToLeftLanguages.Contains(subtags[0]);
        return rightToLeft ? RightToLeft : LeftToRight;
    }
}
