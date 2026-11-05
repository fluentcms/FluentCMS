namespace FluentCMS.Core.Health;

// When the job runner of this instance last showed that its loops are turning. Kept in memory, because a probe asks
// about this process and not about the jobs in the database.
internal sealed class JobRunnerPulse
{
    private long _lastBeatTicks;

    public DateTimeOffset? LastBeat => Interlocked.Read(ref _lastBeatTicks) is var ticks and not 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;

    public void Beat(DateTimeOffset now) => Interlocked.Exchange(ref _lastBeatTicks, now.UtcTicks);
}
