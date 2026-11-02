namespace FluentCMS.Data.Tests.Jobs;

// A clock that moves only when a test says so, and fires the timers that fall due as it moves.
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private readonly Lock _lock = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_lock)
        {
            return _now;
        }
    }

    public void Advance(TimeSpan by)
    {
        DateTimeOffset target;
        lock (_lock)
        {
            target = _now + by;
        }

        while (true)
        {
            ManualTimer? next;
            lock (_lock)
            {
                next = _timers.Where(timer => timer.DueAt <= target).MinBy(timer => timer.DueAt);
                if (next is null)
                {
                    _now = target;
                    return;
                }

                if (next.DueAt > _now)
                {
                    _now = next.DueAt!.Value;
                }

                next.Reschedule(_now);
                if (next.DueAt is null)
                {
                    _timers.Remove(next);
                }
            }

            next.Fire();
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    private sealed class ManualTimer(ManualTimeProvider clock, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan _period = Timeout.InfiniteTimeSpan;

        public DateTimeOffset? DueAt { get; private set; }

        public void Reschedule(DateTimeOffset now) =>
            DueAt = _period == Timeout.InfiniteTimeSpan || _period == TimeSpan.Zero ? null : now + _period;

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (clock._lock)
            {
                _period = period;
                clock._timers.Remove(this);

                if (dueTime == Timeout.InfiniteTimeSpan)
                {
                    DueAt = null;
                    return true;
                }

                DueAt = clock._now + dueTime;
                clock._timers.Add(this);
                return true;
            }
        }

        public void Dispose()
        {
            lock (clock._lock)
            {
                DueAt = null;
                clock._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
