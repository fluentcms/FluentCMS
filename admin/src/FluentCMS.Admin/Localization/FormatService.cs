using System.Globalization;

namespace FluentCMS.Admin.Localization;

// Dates, times and numbers in the user's cultures and time zone (spec 4.17): those of GET /me/preferences once the shell has
// read them, the browser's for any the user has not chosen or the platform does not know. The app's current culture follows
// them through useCulture, which the tests leave out, since it sets the process's default.
internal sealed class FormatService(CultureInfo culture, TimeZoneInfo timeZone, Action<CultureInfo>? useCulture = null) : IAdminFormats
{
    private readonly CultureInfo browserCulture = culture;
    private readonly TimeZoneInfo browserTimeZone = timeZone;

    // Raised once the formats have changed.
    public event Action? Changed;

    // The culture of dates and times.
    public CultureInfo Culture { get; private set; } = culture;

    public CultureInfo NumberCulture { get; private set; } = culture;

    public TimeZoneInfo TimeZone { get; private set; } = timeZone;

    // The IANA id of the zone, which the server takes and every view names.
    public string ZoneId => IanaId(TimeZone);

    string IAdminFormats.TimeZoneId => ZoneId;

    string IAdminFormats.FormatDate(DateTimeOffset value) => Date(value);

    string IAdminFormats.FormatTime(DateTimeOffset value) => Time(value);

    string IAdminFormats.FormatDateTime(DateTimeOffset value) => DateTime(value);

    string IAdminFormats.FormatNumber(decimal value, int? decimals) => Number(value, decimals);

    // The culture of dates with the number format of the culture of numbers, which the app's current culture becomes.
    public CultureInfo Current
    {
        get
        {
            if (Culture.Name == NumberCulture.Name)
            {
                return Culture;
            }

            var combined = (CultureInfo)Culture.Clone();
            combined.NumberFormat = NumberCulture.NumberFormat;
            return CultureInfo.ReadOnly(combined);
        }
    }

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

    // A zone found by its Windows id, as on a desktop, goes by its IANA one.
    internal static string IanaId(TimeZoneInfo zone) =>
        !zone.HasIanaId && TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? iana : zone.Id;

    public void Apply(string? dateCulture, string? numberCulture, string? timeZone)
    {
        Culture = Find(dateCulture) ?? browserCulture;
        NumberCulture = Find(numberCulture) ?? browserCulture;
        TimeZone = FindZone(timeZone) ?? browserTimeZone;
        useCulture?.Invoke(Current);
        Changed?.Invoke();
    }

    // The formats of a choice not saved yet, over the same browser's, for the examples of the account page.
    public FormatService Preview(string? dateCulture, string? numberCulture, string? timeZone)
    {
        var preview = new FormatService(browserCulture, browserTimeZone);
        preview.Apply(dateCulture, numberCulture, timeZone);
        return preview;
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
