using System.Globalization;

namespace FluentCMS.Admin.Localization;

internal static class TextDirection
{
    public const string LeftToRight = "ltr";
    public const string RightToLeft = "rtl";

    public static string Of(CultureInfo culture) => culture.TextInfo.IsRightToLeft ? RightToLeft : LeftToRight;

    // A content locale the culture data does not know is written left to right, as the browser would.
    public static string Of(string locale)
    {
        try
        {
            return Of(CultureInfo.GetCultureInfo(locale));
        }
        catch (CultureNotFoundException)
        {
            return LeftToRight;
        }
    }
}
