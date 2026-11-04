namespace FluentCMS.Publishing;

/// <summary>
/// A publish or an unpublish to run later. With <paramref name="TimeZone"/>, <paramref name="At"/> is a wall-clock time in that zone
/// (<see cref="DateTimeKind.Unspecified"/>) and the zone's rules, daylight saving included, decide the instant. Without one it is that
/// instant (<see cref="DateTimeKind.Utc"/>). A <see cref="DateTimeKind.Local"/> value is refused, because it means something different on each machine.
/// </summary>
/// <param name="Action">What to do.</param>
/// <param name="At">When to do it.</param>
/// <param name="TimeZone">The id of a time zone, such as <c>Europe/Berlin</c>, or <see langword="null"/> when <paramref name="At"/> is UTC. A wall-clock time that happens twice when the clocks go back runs the first time; one that never happens when they go forward is refused.</param>
public sealed record ScheduleRequest(ScheduleAction Action, DateTime At, string? TimeZone = null);
