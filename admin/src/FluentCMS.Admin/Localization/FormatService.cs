using System.Globalization;

namespace FluentCMS.Admin.Localization;

// Dates, times and numbers in the user's cultures and time zone (spec 4.17): those of GET /me/preferences once the shell has
// read them, the browser's for any the user has not chosen or the platform does not know.
internal sealed class FormatService(CultureInfo culture, TimeZoneInfo timeZone)
{
    private readonly CultureInfo browserCulture = culture;
    private readonly TimeZoneInfo browserTimeZone = timeZone;

    // The culture of dates and times.
    public CultureInfo Culture { get; private set; } = culture;

    public CultureInfo NumberCulture { get; private set; } = culture;

    public TimeZoneInfo TimeZone { get; private set; } = timeZone;

    public string Date(DateTimeOffset value) => Local(value).ToString("d", Culture);

    // A date has no time zone: it is the same day everywhere.
    public string Date(DateOnly value) => value.ToString("d", Culture);

    public string Time(DateTimeOffset value) => Local(value).ToString("t", Culture);

    public string DateTime(DateTimeOffset value) => Local(value).ToString("g", Culture);

    // Without a number of decimals, as many as the value has, so that 2.5 is not shown as 2.50.
    public string Number(decimal value, int? decimals = null) =>
        decimals is { } places
            ? value.ToString("N" + places.ToString(CultureInfo.InvariantCulture), NumberCulture)
            : value.ToString("#,0.############################", NumberCulture);

    // The moment in the user's time zone, as a clock there shows it.
    public DateTime Local(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, TimeZone).DateTime;

    public void Apply(string? dateCulture, string? numberCulture, string? timeZone)
    {
        Culture = Find(dateCulture) ?? browserCulture;
        NumberCulture = Find(numberCulture) ?? browserCulture;
        TimeZone = FindZone(timeZone) ?? browserTimeZone;
    }

    private static CultureInfo? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        try
        {
            return CultureInfo.GetCultureInfo(name, predefinedOnly: true);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    private static TimeZoneInfo? FindZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }
    }
}
