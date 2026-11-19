using System.Collections.Concurrent;
using FluentCMS.Content;
using FluentCMS.Sites;

namespace FluentCMS.Plugins.AiAssist;

// What a principal that is over the limit is told: the limit and how long until its next call is let through.
internal readonly record struct AiCallRefusal(int Limit, TimeSpan RetryAfter, bool Report)
{
    public int RetryAfterSeconds => Math.Max(1, (int)Math.Ceiling(RetryAfter.TotalSeconds));
}

// The calls of each principal in each site, counted on this instance, as core counts its rate limits (spec 4.10): a
// bucket holds the limit and fills again over a minute, kept as the time it is next full, so a quiet principal may send
// the limit at once and then one call per minute divided by the limit. A call that is refused takes nothing from a bucket.
// A call is taken before any work is done for it, so that callers sent together cannot all pass, and GiveBack returns it
// when the call is refused before it is sent.
internal sealed class AiCallLimits(TimeProvider time)
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    // Above this many buckets, the full ones, which hold nothing a new one would not, are dropped once a window.
    private const int SweepAbove = 1024;

    private readonly ConcurrentDictionary<(Guid Site, Principal Principal), Bucket> _calls = new();
    private readonly ConcurrentDictionary<Guid, DateOnly> _overBudget = new();
    private long _swept;

    public AiCallRefusal? Take(SiteId site, Principal principal, int limit)
    {
        var now = time.GetUtcNow().UtcTicks;
        Sweep(now);
        var wait = _calls.GetOrAdd((site.Value, principal), static (_, start) => new Bucket(start), now).Take(now, Interval(limit), out var report);
        return wait > 0 ? new AiCallRefusal(limit, TimeSpan.FromTicks(wait), report) : null;
    }

    public void GiveBack(SiteId site, Principal principal, int limit)
    {
        if (_calls.TryGetValue((site.Value, principal), out var bucket))
        {
            bucket.GiveBack(Interval(limit));
        }
    }

    // True for the first refusal of a site over its budget on a day, so that the log names it once a day.
    public bool ReportOverBudget(SiteId site, DateOnly day)
    {
        var reported = false;
        _overBudget.AddOrUpdate(site.Value, _ => { reported = true; return day; }, (_, last) => { reported = last != day; return day; });
        return reported;
    }

    private static long Interval(int limit) => Math.Max(1, Window.Ticks / limit);

    private void Sweep(long now)
    {
        var swept = Interlocked.Read(ref _swept);
        if (_calls.Count <= SweepAbove
            || Math.Abs(now - swept) < Window.Ticks
            || Interlocked.CompareExchange(ref _swept, now, swept) != swept)
        {
            return;
        }

        foreach (var (key, bucket) in _calls)
        {
            if (bucket.IsFull(now))
            {
                _calls.TryRemove(KeyValuePair.Create(key, bucket));
            }
        }
    }

    private sealed class Bucket(long start)
    {
        private readonly Lock _gate = new();
        private long _full = start;
        private long _last = start;
        private long _reported = long.MinValue;

        // Zero when the call is let through, and otherwise the ticks until the next one is. Report is true for the first
        // refusal of a window, so that a caller in a loop is logged once a minute and not on every call.
        public long Take(long now, long interval, out bool report)
        {
            lock (_gate)
            {
                Move(now);
                report = false;
                var start = Math.Max(_full, now);
                var wait = start + interval - now - Window.Ticks;
                if (wait <= 0)
                {
                    _full = start + interval;
                    return 0;
                }

                if (_reported == long.MinValue || Math.Abs(now - _reported) >= Window.Ticks)
                {
                    _reported = now;
                    report = true;
                }

                return wait;
            }
        }

        // What Take took. The bucket fills from the time it is next full, so taking the interval off it is the call not
        // taken; a bucket past full is full.
        public void GiveBack(long interval)
        {
            lock (_gate)
            {
                _full -= interval;
            }
        }

        public bool IsFull(long now)
        {
            lock (_gate)
            {
                Move(now);
                return _full <= now;
            }
        }

        // A clock that moves back moves the bucket with it, so that it neither fills nor empties.
        private void Move(long now)
        {
            if (now < _last)
            {
                _full -= _last - now;
            }

            _last = now;
        }
    }
}
