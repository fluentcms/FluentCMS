namespace FluentCMS.Core.Health;

// When the job runner of this instance last showed that its loops are turning, and how long the oldest pending job of
// each queue had waited when the runner last looked. Kept in memory, because a probe asks about this process and must
// not query the database itself; the runner measures the queues on its maintenance turns.
internal sealed class JobRunnerPulse
{
    private long _lastBeatTicks;

    private IReadOnlyDictionary<string, TimeSpan> _oldestPending = new Dictionary<string, TimeSpan>();

    public DateTimeOffset? LastBeat => Interlocked.Read(ref _lastBeatTicks) is var ticks and not 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;

    public IReadOnlyDictionary<string, TimeSpan> OldestPending => Volatile.Read(ref _oldestPending);

    public void Beat(DateTimeOffset now) => Interlocked.Exchange(ref _lastBeatTicks, now.UtcTicks);

    public void Measure(IReadOnlyDictionary<string, TimeSpan> oldestPending) => Volatile.Write(ref _oldestPending, oldestPending);
}
