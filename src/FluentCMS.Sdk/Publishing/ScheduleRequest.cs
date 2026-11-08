namespace FluentCMS.Publishing;

/// <summary>
/// A publish or an unpublish to run later: at an instant, made with <see cref="At"/>, or at a wall-clock time in a time zone,
/// made with <see cref="AtLocalTime"/>, whose rules, daylight saving included, decide the instant on the day.
/// </summary>
public sealed record ScheduleRequest
{
    private ScheduleRequest(ScheduleAction action, DateTimeOffset? instant, DateTime? localTime, string? timeZone)
    {
        Action = action;
        Instant = instant;
        LocalTime = localTime;
        TimeZone = timeZone;
    }

    /// <summary>What to do.</summary>
    public ScheduleAction Action { get; }

    /// <summary>The instant to do it at, or <see langword="null"/> for a request made with <see cref="AtLocalTime"/>.</summary>
    public DateTimeOffset? Instant { get; }

    /// <summary>The wall-clock time in <see cref="TimeZone"/>, of kind <see cref="DateTimeKind.Unspecified"/>, or <see langword="null"/> for a request made with <see cref="At"/>.</summary>
    public DateTime? LocalTime { get; }

    /// <summary>The IANA id of the time zone of <see cref="LocalTime"/>, such as <c>Europe/Berlin</c>, or <see langword="null"/> for a request made with <see cref="At"/>.</summary>
    public string? TimeZone { get; }

    /// <summary>Runs the action at an instant. Its offset only says how the instant is written, and is not kept.</summary>
    /// <param name="action">What to do.</param>
    /// <param name="instant">When to do it.</param>
    /// <returns>The request.</returns>
    public static ScheduleRequest At(ScheduleAction action, DateTimeOffset instant) => new(action, instant, null, null);

    /// <summary>
    /// Runs the action when the clocks of a time zone show a time. A time that happens twice when the clocks go back runs the
    /// first time; one that never happens when they go forward is refused.
    /// </summary>
    /// <param name="action">What to do.</param>
    /// <param name="localTime">The wall-clock time. Its <see cref="DateTime.Kind"/> is ignored: the value is read as the time the clocks show.</param>
    /// <param name="timeZone">The IANA id of the time zone, such as <c>Europe/Berlin</c>.</param>
    /// <returns>The request.</returns>
    public static ScheduleRequest AtLocalTime(ScheduleAction action, DateTime localTime, string timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        return new(action, null, DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified), timeZone);
    }
}
