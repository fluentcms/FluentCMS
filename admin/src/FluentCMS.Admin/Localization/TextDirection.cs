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

    // An explicit script decides (ar-Latn is left to right, az-Arab right to left); otherwise the language. The script is the
    // subtag right after the language and its extended language subtags (BCP 47), so a later one, such as an extension's
    // after a singleton (ar-u-nu-latn, en-x-arab), is not.
    public static string Of(string languageTag)
    {
        var subtags = languageTag.Split('-', '_');
        var next = subtags.Skip(1).SkipWhile((subtag, index) => index < 3 && subtag.Length == 3 && subtag.All(char.IsAsciiLetter)).FirstOrDefault();
        var script = next is { Length: 4 } && next.All(char.IsAsciiLetter) ? next : null;
        var rightToLeft = script is not null ? RightToLeftScripts.Contains(script) : RightToLeftLanguages.Contains(subtags[0]);
        return rightToLeft ? RightToLeft : LeftToRight;
    }
}
