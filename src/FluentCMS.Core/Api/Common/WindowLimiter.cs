using System.Collections.Concurrent;
using System.Threading.RateLimiting;

namespace FluentCMS.Core.Api.Common;

// A fixed window limiter that keeps its window itself, so that a response can carry RateLimit-Reset and Retry-After and
// no timer runs per caller. A window starts with the first request after the previous one ended. Once the window is over
// and nothing came in, the limiter reports how long it has been idle, and the partitioned limiter disposes it.
internal sealed class WindowLimiter : RateLimiter
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly Lock _gate = new();
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, WindowLimiter> _owner;
    private readonly string _key;
    private DateTimeOffset _start;
    private int _used;
    private long _successful;
    private long _failed;

    public WindowLimiter(string key, int permitLimit, TimeProvider time, ConcurrentDictionary<string, WindowLimiter> owner)
    {
        _key = key;
        _owner = owner;
        _time = time;
        Limit = permitLimit;
        _start = time.GetUtcNow();
    }

    public int Limit { get; }

    public int Remaining
    {
        get
        {
            lock (_gate)
            {
                Roll(_time.GetUtcNow());
                return Math.Max(0, Limit - _used);
            }
        }
    }

    public int SecondsToReset
    {
        get
        {
            lock (_gate)
            {
                var now = _time.GetUtcNow();
                Roll(now);
                return Math.Max(1, (int)Math.Ceiling((_start + Window - now).TotalSeconds));
            }
        }
    }

    public override TimeSpan? IdleDuration
    {
        get
        {
            lock (_gate)
            {
                var over = _time.GetUtcNow() - (_start + Window);
                return over > TimeSpan.Zero ? over : null;
            }
        }
    }

    public override RateLimiterStatistics? GetStatistics()
    {
        lock (_gate)
        {
            Roll(_time.GetUtcNow());
            return new RateLimiterStatistics
            {
                CurrentAvailablePermits = Math.Max(0, Limit - _used),
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
            var now = _time.GetUtcNow();
            Roll(now);
            if (_used + permitCount <= Limit)
            {
                _used += permitCount;
                _successful++;
                return new Lease(acquired: true, retryAfter: null);
            }

            _failed++;
            return new Lease(acquired: false, _start + Window - now);
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

    private void Release() => _owner.TryRemove(new KeyValuePair<string, WindowLimiter>(_key, this));

    // A clock that moved back starts a new window too, so that a limiter cannot be stuck in the future.
    private void Roll(DateTimeOffset now)
    {
        if (now >= _start + Window || now < _start)
        {
            _start = now;
            _used = 0;
        }
    }

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
