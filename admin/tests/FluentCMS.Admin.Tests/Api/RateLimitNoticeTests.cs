using FluentCMS.Admin.Api;
using FluentCMS.Admin.Localization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Api;

public sealed class RateLimitNoticeTests
{
    private readonly FakeAnnouncer _announcer = new();
    private readonly ManualTime _time = new();
    private readonly RateLimitNotice _notice;

    public RateLimitNoticeTests()
    {
        var factory = new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        _notice = new RateLimitNotice(_announcer, new StringLocalizer<AdminResources>(factory), _time);
    }

    [Fact]
    public void TheNoticeIsOneAssertiveAnnouncementThatSaysHowLongToWait()
    {
        _notice.TooManyRequests(TimeSpan.FromSeconds(29.2));

        var (message, politeness) = _announcer.Announcements.ShouldHaveSingleItem();
        politeness.ShouldBe(AnnouncementPoliteness.Assertive);
        message.ShouldBe("Too many requests. Wait 30 seconds and try again.");
    }

    [Fact]
    public void WithoutAWaitTheNoticeSaysAMoment()
    {
        _notice.TooManyRequests(null);

        _announcer.Announcements.ShouldHaveSingleItem().Message.ShouldBe("Too many requests. Wait a moment and try again.");
    }

    [Fact]
    public void ABurstIsAnnouncedOnce()
    {
        _notice.TooManyRequests(TimeSpan.FromSeconds(5));
        _time.Advance(RateLimitNotice.Quiet - TimeSpan.FromSeconds(1));
        _notice.TooManyRequests(TimeSpan.FromSeconds(5));
        _notice.TooManyRequests(null);
        _time.Advance(RateLimitNotice.Quiet);
        _notice.TooManyRequests(TimeSpan.FromSeconds(5));

        _announcer.Announcements.Count.ShouldBe(2);
    }

    private sealed class FakeAnnouncer : IAnnouncer
    {
        public List<(string Message, AnnouncementPoliteness Politeness)> Announcements { get; } = [];

        public void Announce(string message, AnnouncementPoliteness politeness = AnnouncementPoliteness.Polite) => Announcements.Add((message, politeness));
    }

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
