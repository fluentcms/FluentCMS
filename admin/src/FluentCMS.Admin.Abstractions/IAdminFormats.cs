namespace FluentCMS.Admin;

/// <summary>
/// Dates, times and numbers as the signed-in user chose to see them on the account page: in the culture and the time zone of
/// <c>GET /me/preferences</c>, and the browser's for those the user has not chosen. A key or a service account has the
/// browser's. A change applies from the next text formatted, without a reload.
/// </summary>
public interface IAdminFormats
{
    /// <summary>Gets the IANA id of the user's time zone, such as <c>Europe/Berlin</c>, as the server takes a zone.</summary>
    string TimeZoneId { get; }

    /// <summary>Formats the day of a moment in the user's time zone, in the short date pattern of the user's culture of dates.</summary>
    /// <param name="value">The moment.</param>
    /// <returns>The date as the user reads it.</returns>
    string FormatDate(DateTimeOffset value);

    /// <summary>Formats the time of a moment in the user's time zone, in the short time pattern of the user's culture of dates.</summary>
    /// <param name="value">The moment.</param>
    /// <returns>The time as the user reads it.</returns>
    string FormatTime(DateTimeOffset value);

    /// <summary>Formats a moment in the user's time zone, with its date and its time, in the user's culture of dates.</summary>
    /// <param name="value">The moment.</param>
    /// <returns>The date and time as the user reads them.</returns>
    string FormatDateTime(DateTimeOffset value);

    /// <summary>Formats a number with group separators in the user's culture of numbers.</summary>
    /// <param name="value">The number.</param>
    /// <param name="decimals">The number of decimals shown, or <see langword="null"/> for as many as the value has.</param>
    /// <returns>The number as the user reads it.</returns>
    string FormatNumber(decimal value, int? decimals = null);
}
