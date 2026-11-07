using System.Collections.Concurrent;
using System.Threading.RateLimiting;

namespace FluentCMS.Core.Api.Common;

// A token bucket that holds the limit and refills it over a minute, kept as the time at which the bucket is next full
// (the generic cell rate algorithm), so that one number is the whole state and no timer runs per caller. A quiet caller
// may send the limit at once, and after that one request per minute divided by the limit: a fixed window would let it
// send the limit again as soon as the window turned, twice the limit within a second. A clock that moves back moves the
// bucket with it, so that it neither fills nor empties. A full bucket reports how long it has been full, and the
// partitioned limiter disposes it, which loses nothing, since a new one starts full.
internal sealed class TokenBucketLimiter : RateLimiter
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly Lock _gate = new();
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, TokenBucketLimiter> _owner;
    private readonly string _key;
    private readonly long _interval;
    private long _full;
    private long _last;
    private long _successful;
    private long _failed;

    public TokenBucketLimiter(string key, int permitLimit, TimeProvider time, ConcurrentDictionary<string, TokenBucketLimiter> owner)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(permitLimit, 1);

        _key = key;
        _owner = owner;
        _time = time;
        Limit = permitLimit;
        _interval = Math.Max(1, Window.Ticks / permitLimit);
        _full = _last = time.GetUtcNow().UtcTicks;
    }

    public int Limit { get; }

    public int Remaining
    {
        get
        {
            lock (_gate)
            {
                return RemainingAt(Now());
            }
        }
    }

    // Until the bucket is full again, which is what RateLimit-Reset says.
    public int SecondsToReset
    {
        get
        {
            lock (_gate)
            {
                return Seconds(_full - Now());
            }
        }
    }

    // Until the next request is let through, which is what Retry-After says.
    public int SecondsToRetry
    {
        get
        {
            lock (_gate)
            {
                var now = Now();
                return Seconds(Math.Max(_full, now) + _interval - now - Window.Ticks);
            }
        }
    }

    public override TimeSpan? IdleDuration
    {
        get
        {
            lock (_gate)
            {
                var idle = Now() - _full;
                return idle > 0 ? TimeSpan.FromTicks(idle) : null;
            }
        }
    }

    public override RateLimiterStatistics? GetStatistics()
    {
        lock (_gate)
        {
            return new RateLimiterStatistics
            {
                CurrentAvailablePermits = RemainingAt(Now()),
                CurrentQueuedCount = 0,
                TotalFailedLeases = _failed,
                TotalSuccessfulLeases = _successful,
            };
        }
    }

    protected override RateLimitLease AttemptAcquireCore(int permitCount)
    {
        lock (_gate)
        {
            // Asking for no permits asks whether one is there.
            var now = Now();
            var start = Math.Max(_full, now);
            var wait = start + (Math.Max(permitCount, 1) * _interval) - now - Window.Ticks;
            if (permitCount <= Limit && wait <= 0)
            {
                _full = start + (permitCount * _interval);
                _successful++;
                return new Lease(acquired: true, retryAfter: null);
            }

            _failed++;
            return new Lease(acquired: false, TimeSpan.FromTicks(Math.Max(1, wait)));
        }
    }

    protected override ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken) =>
        ValueTask.FromResult(AttemptAcquireCore(permitCount));

    protected override void Dispose(bool disposing)
    {
        Release();
        base.Dispose(disposing);
    }

    // The partitioned limiter disposes an idle partition with DisposeAsync, which does not reach Dispose(true).
    protected override ValueTask DisposeAsyncCore()
    {
        Release();
        return base.DisposeAsyncCore();
    }

    private void Release() => _owner.TryRemove(new KeyValuePair<string, TokenBucketLimiter>(_key, this));

    private long Now()
    {
        var now = _time.GetUtcNow().UtcTicks;
        if (now < _last)
        {
            _full -= _last - now;
        }

        _last = now;
        return now;
    }

    private int RemainingAt(long now) => (int)Math.Clamp((Window.Ticks - Math.Max(0, _full - now)) / _interval, 0, Limit);

    private static int Seconds(long ticks) => Math.Max(1, (int)Math.Ceiling((double)ticks / TimeSpan.TicksPerSecond));

    private sealed class Lease(bool acquired, TimeSpan? retryAfter) : RateLimitLease
    {
        public override bool IsAcquired => acquired;

        public override IEnumerable<string> MetadataNames => retryAfter is null ? [] : [MetadataName.RetryAfter.Name];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (retryAfter is { } value && metadataName == MetadataName.RetryAfter.Name)
            {
                metadata = value;
                return true;
            }

            metadata = null;
            return false;
        }
    }
}
