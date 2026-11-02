using System.Collections.ObjectModel;
using System.Globalization;

namespace FluentCMS.Schema;

// Reduces a CLR value to what a schema file can hold: null, string, bool, long, decimal, or a list of strings.
internal static class SchemaValue
{
    public static object? Normalize(object? value, bool allowList = false)
    {
        switch (value)
        {
            case null:
                return null;
            case string text:
                return text;
            case char c:
                return c.ToString();
            case bool flag:
                return flag;
            case sbyte or byte or short or ushort or int or uint or long:
                return Convert.ToInt64(value, CultureInfo.InvariantCulture);
            case ulong big:
                return FromDecimal(big);
            case float or double:
                return FromDecimal(ToDecimal(Convert.ToDouble(value, CultureInfo.InvariantCulture)));
            case decimal number:
                return FromDecimal(number);
            case Enum member:
                return SchemaNaming.ToCamelCase(member.ToString());
            case Guid id:
                return id.ToString("D", CultureInfo.InvariantCulture);
            case DateTimeOffset offset:
                return offset.ToString("O", CultureInfo.InvariantCulture);
            case DateTime date:
                return date.ToString("O", CultureInfo.InvariantCulture);
            case DateOnly day:
                return day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            case TimeOnly time:
                return time.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            case IEnumerable<string> list when allowList:
                return new ReadOnlyCollection<string>([.. list]);
            default:
                throw new ArgumentException($"A value of type {value.GetType().Name} cannot be stored in a schema.", nameof(value));
        }
    }

    public static decimal NormalizeDecimal(decimal value) => value / 1.0000000000000000000000000000m;

    private static object FromDecimal(decimal value)
    {
        var normalized = NormalizeDecimal(value);
        var isWholeNumber = normalized == decimal.Truncate(normalized) && normalized >= long.MinValue && normalized <= long.MaxValue;
        return isWholeNumber ? Box((long)normalized) : Box(normalized);
    }

    private static object Box<T>(T value)
        where T : struct => value;

    private static decimal ToDecimal(double value)
    {
        try
        {
            return (decimal)value;
        }
        catch (OverflowException)
        {
            throw new ArgumentException($"The number {value.ToString(CultureInfo.InvariantCulture)} cannot be stored in a schema.", nameof(value));
        }
    }
}
