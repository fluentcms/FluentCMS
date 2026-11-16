using System.Globalization;

namespace FluentCMS.Admin.Localization;

// Dates, times and numbers in the user's culture and time zone: the browser's until the preferences of #3096 (spec 4.17).
internal sealed class FormatService(CultureInfo culture, TimeZoneInfo timeZone)
{
    public CultureInfo Culture { get; } = culture;

    public TimeZoneInfo TimeZone { get; } = timeZone;

    public string Date(DateTimeOffset value) => Local(value).ToString("d", Culture);

    public string Time(DateTimeOffset value) => Local(value).ToString("t", Culture);

    public string DateTime(DateTimeOffset value) => Local(value).ToString("g", Culture);

    // Without a number of decimals, as many as the value has, so that 2.5 is not shown as 2.50.
    public string Number(decimal value, int? decimals = null) =>
        decimals is { } places
            ? value.ToString("N" + places.ToString(CultureInfo.InvariantCulture), Culture)
            : value.ToString("#,0.############################", Culture);

    private DateTimeOffset Local(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, TimeZone);
}
