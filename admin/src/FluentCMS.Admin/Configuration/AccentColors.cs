using System.Globalization;

namespace FluentCMS.Admin.Configuration;

// What the branding's accent brings with it (spec 2.3): the color on the accent, black or white, whichever contrasts more by
// WCAG's relative luminance, which gives at least 4.58:1 for any accent; and the shade its hover and pressed states mix in,
// the other of the two, so that a state contrasts with the color on it at least as much as the accent does. An alpha is
// left out: the accent is taken as opaque.
internal static class AccentColors
{
    public const string OnAccentProperty = "--fluentcms-brand-on-accent";
    public const string ShadeProperty = "--fluentcms-brand-accent-shade";

    private const string Black = "#000000";
    private const string White = "#ffffff";

    public static (string OnAccent, string Shade) Of(string accent)
    {
        var luminance = Luminance(accent);
        return (luminance + 0.05) / 0.05 >= 1.05 / (luminance + 0.05) ? (Black, White) : (White, Black);
    }

    public static double Luminance(string hex)
    {
        var digits = hex.TrimStart('#');
        if (digits.Length is 3 or 4)
        {
            digits = string.Concat(digits[..3].Select(digit => new string(digit, 2)));
        }

        return (0.2126 * Channel(digits, 0)) + (0.7152 * Channel(digits, 2)) + (0.0722 * Channel(digits, 4));
    }

    private static double Channel(string digits, int start)
    {
        var value = int.Parse(digits.AsSpan(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
