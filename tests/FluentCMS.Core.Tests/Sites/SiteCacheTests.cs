using FluentCMS.Core.Sites;
using FluentCMS.Events;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Sites;

public sealed class SiteCacheTests
{
    private static readonly Guid Site = Guid.CreateVersion7();

    private readonly Clock _clock = new(new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero));
    private readonly SiteCache _cache;

    public SiteCacheTests() => _cache = new SiteCache(_clock);

    [Fact]
    public void AValueIsKeptForTheCacheDurationAndNoLonger()
    {
        _cache.Set(SiteCache.SiteKey(Site), true, _cache.Generation);

        _clock.Now += SiteCache.CacheDuration - TimeSpan.FromSeconds(1);
        _cache.TryGet<bool>(SiteCache.SiteKey(Site), out _).ShouldBeTrue();

        _clock.Now += TimeSpan.FromSeconds(1);
        _cache.TryGet<bool>(SiteCache.SiteKey(Site), out _).ShouldBeFalse();
    }

    [Fact]
    public void InvalidateDropsEveryValue()
    {
        _cache.Set(SiteCache.DefaultKey, Site, _cache.Generation);
        _cache.Set(SiteCache.SlugKey("docs"), Site, _cache.Generation);

        _cache.Invalidate();

        _cache.TryGet<Guid>(SiteCache.DefaultKey, out _).ShouldBeFalse();
        _cache.TryGet<Guid>(SiteCache.SlugKey("docs"), out _).ShouldBeFalse();
    }

    // The read began before the write that invalidated, so what it found may be the old row.
    [Fact]
    public void AReadThatWasUnderWayWhenTheCacheWasInvalidatedIsNotKept()
    {
        var generation = _cache.Generation;
        _cache.Invalidate();

        _cache.Set(SiteCache.SlugKey("docs"), Site, generation);

        _cache.TryGet<Guid>(SiteCache.SlugKey("docs"), out _).ShouldBeFalse();
    }

    [Fact]
    public async Task ASiteUpdatedEventRaisedHereDropsEveryValue()
    {
        _cache.Set(SiteCache.LocalesKey(Site), "kept", _cache.Generation);

        await new SiteCacheInvalidation(_cache).HandleAsync(new SiteUpdatedEvent(Site, "docs"), TestContext.Current.CancellationToken);

        _cache.TryGet<string>(SiteCache.LocalesKey(Site), out _).ShouldBeFalse();
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
