namespace FluentCMS.Testing;

// Only the wall clock is controlled. Timers and delays stay real, so a background loop that waits on this provider keeps
// polling; a test moves the clock and the next poll sees it.
public sealed class ControllableTimeProvider(DateTimeOffset start) : TimeProvider
{
    private long _utcTicks = start.UtcTicks;

    public ControllableTimeProvider()
        : this(DateTimeOffset.UtcNow)
    {
    }

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);

    public void Set(DateTimeOffset utcNow) => Interlocked.Exchange(ref _utcTicks, utcNow.UtcTicks);

    public void Advance(TimeSpan by) => Interlocked.Add(ref _utcTicks, by.Ticks);
}
