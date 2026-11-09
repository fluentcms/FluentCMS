using System.Collections.Concurrent;
using System.Net;
using System.Threading.RateLimiting;
using FluentCMS.Core.Api.Common;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Api.Common;

public sealed class TokenBucketLimiterTests
{
    private readonly ClockProvider _time = new();
    private readonly ConcurrentDictionary<string, TokenBucketLimiter> _owner = new();

    [Fact]
    public void ALimiterGrantsItsLimitAtOnceAndThenOnePermitPerShareOfTheMinute()
    {
        var limiter = Create("a", limit: 2);

        limiter.AttemptAcquire().IsAcquired.ShouldBeTrue();
        limiter.AttemptAcquire().IsAcquired.ShouldBeTrue();
        limiter.Remaining.ShouldBe(0);
        using var refused = limiter.AttemptAcquire();

        refused.IsAcquired.ShouldBeFalse();
        refused.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter).ShouldBeTrue();
        retryAfter.ShouldBe(TimeSpan.FromSeconds(30));
        limiter.SecondsToRetry.ShouldBe(30);
        limiter.SecondsToReset.ShouldBe(60);

        _time.Advance(TimeSpan.FromSeconds(30));

        limiter.Remaining.ShouldBe(1);
        limiter.SecondsToReset.ShouldBe(30);
        limiter.AttemptAcquire().IsAcquired.ShouldBeTrue();
        limiter.AttemptAcquire().IsAcquired.ShouldBeFalse();
    }

    // A fixed window lets the limit through at the end of one window and again at the start of the next.
    [Fact]
    public void ABurstAtTheTurnOfAMinuteIsNotLetThroughTwice()
    {
        var limiter = Create("a", limit: 60);
        _time.Advance(TimeSpan.FromSeconds(59));

        Enumerable.Range(0, 60).Count(_ => limiter.AttemptAcquire().IsAcquired).ShouldBe(60);
        _time.Advance(TimeSpan.FromSeconds(1));

        Enumerable.Range(0, 60).Count(_ => limiter.AttemptAcquire().IsAcquired).ShouldBe(1);
        _time.Advance(TimeSpan.FromSeconds(30));
        Enumerable.Range(0, 60).Count(_ => limiter.AttemptAcquire().IsAcquired).ShouldBe(30);
    }

    [Fact]
    public void TheBucketFillsAgainOverAMinuteAndHoldsNoMoreThanTheLimit()
    {
        var limiter = Create("a", limit: 3);
        Enumerable.Range(0, 3).Count(_ => limiter.AttemptAcquire().IsAcquired).ShouldBe(3);

        _time.Advance(TokenBucketLimiter.Window * 5);

        limiter.Remaining.ShouldBe(3);
        Enumerable.Range(0, 4).Count(_ => limiter.AttemptAcquire().IsAcquired).ShouldBe(3);
    }

    [Fact]
    public void AClockThatMovesBackKeepsTheBucketAsItWas()
    {
        var limiter = Create("a", limit: 1);
        limiter.AttemptAcquire().IsAcquired.ShouldBeTrue();

        _time.Advance(TimeSpan.FromHours(-1));

        limiter.AttemptAcquire().IsAcquired.ShouldBeFalse();
        limiter.SecondsToRetry.ShouldBe(60);
        _time.Advance(TokenBucketLimiter.Window);
        limiter.AttemptAcquire().IsAcquired.ShouldBeTrue();
    }

    [Fact]
    public void AskingForNoPermitsSaysWhetherOneIsThere()
    {
        var limiter = Create("a", limit: 1);

        limiter.AttemptAcquire(0).IsAcquired.ShouldBeTrue();
        limiter.Remaining.ShouldBe(1);
        limiter.AttemptAcquire().IsAcquired.ShouldBeTrue();
        limiter.AttemptAcquire(0).IsAcquired.ShouldBeFalse();
    }

    [Fact]
    public void ALimiterIsNotIdleUntilItsBucketIsFullAndIsIdleForTheTimeSince()
    {
        var limiter = Create("a", limit: 2);
        limiter.AttemptAcquire();

        limiter.IdleDuration.ShouldBeNull();
        _time.Advance(TimeSpan.FromSeconds(20));
        limiter.IdleDuration.ShouldBeNull();

        _time.Advance(TimeSpan.FromSeconds(40));

        limiter.IdleDuration.ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task DisposeAsyncReleasesTheLimiterBecauseThatIsHowAnIdlePartitionIsDisposed()
    {
        var limiter = Create("a", limit: 1);

        await limiter.DisposeAsync();

        _owner.ShouldBeEmpty();
    }

    [Fact]
    public void DisposeReleasesTheLimiter()
    {
        var limiter = Create("a", limit: 1);

        limiter.Dispose();

        _owner.ShouldBeEmpty();
    }

    [Fact]
    public void AReplacedLimiterIsNotRemovedByTheOldOnesDisposal()
    {
        var old = Create("a", limit: 1);
        var current = Create("a", limit: 1);

        old.Dispose();

        _owner["a"].ShouldBeSameAs(current);
    }

    [Fact]
    public async Task APartitionedLimiterReleasesAnIdlePartitionOnceItsBucketIsFull()
    {
        var released = new Releases(3);
        using var partitioned = PartitionedRateLimiter.Create<string, string>(key =>
            RateLimitPartition.Get(key, name => new Releasing(Create(name, limit: 1), released)));
        foreach (var key in new[] { "a", "b", "c" })
        {
            partitioned.AttemptAcquire(key).Dispose();
        }

        _owner.Count.ShouldBe(3);
        _time.Advance(TokenBucketLimiter.Window + TimeSpan.FromMinutes(1));

        // The partitioned limiter looks for idle partitions every few seconds on its own timer.
        await released.All.Task.WaitAsync(TimeSpan.FromSeconds(40), TestContext.Current.CancellationToken);

        _owner.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("10.0.0.1", "10.0.0.1")]
    [InlineData("::ffff:10.0.0.1", "10.0.0.1")]
    [InlineData("2001:db8:1:2:3:4:5:6", "2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:2:ffff:ffff:ffff:ffff", "2001:db8:1:2::/64")]
    [InlineData("fe80::1%3", "fe80::/64")]
    public void AnAddressIsPartitionedByItsIpv4AddressOrIpv6Prefix(string address, string expected) =>
        ClientAddress.Partition(IPAddress.Parse(address)).ShouldBe(expected);

    [Fact]
    public void AddressesOfOneIpv6PrefixShareAPartitionAndOtherPrefixesDoNot()
    {
        var first = ClientAddress.Partition(IPAddress.Parse("2001:db8:1:2::1"));
        var same = ClientAddress.Partition(IPAddress.Parse("2001:db8:1:2:aaaa::2"));
        var other = ClientAddress.Partition(IPAddress.Parse("2001:db8:1:3::1"));

        same.ShouldBe(first);
        other.ShouldNotBe(first);
        ClientAddress.Partition(null).ShouldBe("unknown");
    }

    private TokenBucketLimiter Create(string key, int limit)
    {
        var limiter = new TokenBucketLimiter(key, limit, _time, _owner);
        _owner[key] = limiter;
        return limiter;
    }

    private sealed class Releases(int partitions)
    {
        private int _left = partitions;

        public TaskCompletionSource All { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void One()
        {
            if (Interlocked.Decrement(ref _left) == 0)
            {
                All.TrySetResult();
            }
        }
    }

    // A partition that says when the partitioned limiter lets it go, which it does with DisposeAsync.
    private sealed class Releasing(TokenBucketLimiter inner, Releases releases) : RateLimiter
    {
        public override TimeSpan? IdleDuration => inner.IdleDuration;

        public override RateLimiterStatistics? GetStatistics() => inner.GetStatistics();

        protected override RateLimitLease AttemptAcquireCore(int permitCount) => inner.AttemptAcquire(permitCount);

        protected override ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken) =>
            inner.AcquireAsync(permitCount, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        protected override async ValueTask DisposeAsyncCore()
        {
            await inner.DisposeAsync();
            await base.DisposeAsyncCore();
            releases.One();
        }
    }

    private sealed class ClockProvider : TimeProvider
    {
        private long _ticks = new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero).UtcTicks;

        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

        public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
    }
}
