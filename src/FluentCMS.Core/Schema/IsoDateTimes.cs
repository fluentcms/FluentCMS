using System.Globalization;
using System.Text.RegularExpressions;

namespace FluentCMS.Core.Schema;

// The forms of time the schema document promises agents (SCH-06): a point in time in ISO 8601 with an offset, and a calendar date.
internal static partial class IsoDateTimes
{
    public static bool IsPointInTime(string? text)
    {
        return text is not null
            && PointInTimePattern().IsMatch(text)
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    }

    public static bool IsDate(string? text)
    {
        return DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})\z", RegexOptions.CultureInvariant)]
    private static partial Regex PointInTimePattern();
}
