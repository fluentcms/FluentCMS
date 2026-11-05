using System.Collections.Concurrent;
using System.Net;
using System.Threading.RateLimiting;
using FluentCMS.Core.Api.Common;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Api.Common;

public sealed class WindowLimiterTests
{
    private readonly ClockProvider _time = new();
    private readonly ConcurrentDictionary<string, WindowLimiter> _owner = new();

    [Fact]
    public void ALimiterGrantsItsLimitPerWindowAndThenRefusesWithTheTimeLeft()
    {
        var limiter = Create("a", limit: 2);

        limiter.AttemptAcquire().IsAcquired.ShouldBeTrue();
        _time.Advance(TimeSpan.FromSeconds(10));
        limiter.AttemptAcquire().IsAcquired.ShouldBeTrue();
        limiter.Remaining.ShouldBe(0);
        using var refused = limiter.AttemptAcquire();

        refused.IsAcquired.ShouldBeFalse();
        refused.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter).ShouldBeTrue();
        retryAfter.ShouldBe(TimeSpan.FromSeconds(50));
        limiter.SecondsToReset.ShouldBe(50);
    }

    [Fact]
    public void ANewWindowStartsAfterTheOldOneEnds()
    {
        var limiter = Create("a", limit: 1);
        limiter.AttemptAcquire().IsAcquired.ShouldBeTrue();
        limiter.AttemptAcquire().IsAcquired.ShouldBeFalse();

        _time.Advance(WindowLimiter.Window);

        limiter.Remaining.ShouldBe(1);
        limiter.AttemptAcquire().IsAcquired.ShouldBeTrue();
    }

    [Fact]
    public void AClockThatMovesBackStartsANewWindow()
    {
        var limiter = Create("a", limit: 1);
        limiter.AttemptAcquire().IsAcquired.ShouldBeTrue();

        _time.Advance(TimeSpan.FromHours(-1));

        limiter.AttemptAcquire().IsAcquired.ShouldBeTrue();
    }

    [Fact]
    public void ALimiterIsNotIdleWhileItsWindowRunsAndIsIdleForTheTimeSinceItEnded()
    {
        var limiter = Create("a", limit: 1);
        limiter.AttemptAcquire();

        limiter.IdleDuration.ShouldBeNull();

        _time.Advance(WindowLimiter.Window + TimeSpan.FromSeconds(30));

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
    public async Task APartitionedLimiterReleasesAnIdlePartitionAfterItsWindow()
    {
        using var partitioned = PartitionedRateLimiter.Create<string, string>(key =>
            RateLimitPartition.Get(key, name => Create(name, limit: 1)));
        foreach (var key in new[] { "a", "b", "c" })
        {
            partitioned.AttemptAcquire(key).Dispose();
        }

        _owner.Count.ShouldBe(3);
        _time.Advance(WindowLimiter.Window + TimeSpan.FromMinutes(1));

        // The partitioned limiter looks for idle partitions every few seconds on its own timer.
        var until = DateTime.UtcNow.AddSeconds(40);
        while (!_owner.IsEmpty && DateTime.UtcNow < until)
        {
            await Task.Delay(500, TestContext.Current.CancellationToken);
        }

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

    private WindowLimiter Create(string key, int limit)
    {
        var limiter = new WindowLimiter(key, limit, _time, _owner);
        _owner[key] = limiter;
        return limiter;
    }

    private sealed class ClockProvider : TimeProvider
    {
        private long _ticks = new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero).UtcTicks;

        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

        public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
    }
}
