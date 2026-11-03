namespace FluentCMS.Publishing;

/// <summary>A schedule that has not come due.</summary>
/// <param name="Action">What it does.</param>
/// <param name="Locale">The locale it is for; empty for a type that is not localized.</param>
/// <param name="RunAt">The instant it runs.</param>
/// <param name="TimeZone">The time zone it was given in, or <see langword="null"/> for a UTC time.</param>
/// <param name="Version">The draft version when it was scheduled. A publish takes the draft as it is when it runs.</param>
public sealed record ScheduleInfo(ScheduleAction Action, string Locale, DateTimeOffset RunAt, string? TimeZone, int Version);
