using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.RateLimiting;

namespace FluentCMS.Core.Api.Common;

// A fixed window limiter that can say how long its window has left, which the built-in one does not, so that a response
// can carry RateLimit-Reset and Retry-After. The window timer starts with the limiter, so the window ends are known.
internal sealed class WindowLimiter : RateLimiter
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly FixedWindowRateLimiter _inner;
    private readonly long _started = Stopwatch.GetTimestamp();
    private readonly ConcurrentDictionary<string, WindowLimiter> _owner;
    private readonly string _key;

    public WindowLimiter(string key, int permitLimit, ConcurrentDictionary<string, WindowLimiter> owner)
    {
        _key = key;
        _owner = owner;
        Limit = permitLimit;
        _inner = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = Window,
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    }

    public int Limit { get; }

    public int Remaining => (int)(_inner.GetStatistics()?.CurrentAvailablePermits ?? 0);

    public int SecondsToReset
    {
        get
        {
            var elapsed = Stopwatch.GetElapsedTime(_started);
            var left = Window - TimeSpan.FromTicks(elapsed.Ticks % Window.Ticks);
            return Math.Max(1, (int)Math.Ceiling(left.TotalSeconds));
        }
    }

    public override TimeSpan? IdleDuration => _inner.IdleDuration;

    public override RateLimiterStatistics? GetStatistics() => _inner.GetStatistics();

    protected override RateLimitLease AttemptAcquireCore(int permitCount) => _inner.AttemptAcquire(permitCount);

    protected override ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken) =>
        _inner.AcquireAsync(permitCount, cancellationToken);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _owner.TryRemove(new KeyValuePair<string, WindowLimiter>(_key, this));
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
