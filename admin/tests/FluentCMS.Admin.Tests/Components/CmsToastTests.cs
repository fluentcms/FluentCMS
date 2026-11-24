using Bunit;
using FluentCMS.Admin.Components;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Components;

// A toast of the wrapped set (spec 4.18): read once through the host's announcer, an error from the assertive region, and no
// live region itself; an error stays until it is dismissed, the others go after their lifetime.
public sealed class CmsToastTests : ComponentTestContext
{
    private readonly Announcements announced = new();
    private readonly ManualTime time = new();

    public CmsToastTests()
    {
        Services.AddSingleton<IAnnouncer>(announced);
        Services.AddSingleton<TimeProvider>(time);
    }

    [Theory]
    [InlineData("Saved \"a < b\"")]
    [InlineData("Published <b>x</b>")]
    [InlineData("R&D &copy; 2026")]
    public void AToastShowsItsMessageAsText(string message)
    {
        var root = Render<CmsThemeRoot>();

        Show(root, message);

        root.WaitForAssertion(() => root.Find(".cms-toast-message").TextContent.ShouldBe(message));
        root.FindAll(".cms-toast b").ShouldBeEmpty();
    }

    [Theory]
    [InlineData(CmsToastIntent.Info, AnnouncementPoliteness.Polite)]
    [InlineData(CmsToastIntent.Success, AnnouncementPoliteness.Polite)]
    [InlineData(CmsToastIntent.Warning, AnnouncementPoliteness.Polite)]
    [InlineData(CmsToastIntent.Error, AnnouncementPoliteness.Assertive)]
    public void AToastIsAnnouncedOnceInThePolitenessOfItsIntentAndIsNoLiveRegionOfItsOwn(CmsToastIntent intent, AnnouncementPoliteness politeness)
    {
        var root = Render<CmsThemeRoot>();

        Show(root, "Saved.", intent);

        root.WaitForAssertion(() => root.Find(".cms-toast").GetAttribute("data-intent").ShouldBe(intent.ToString().ToLowerInvariant()));
        announced.Messages.ShouldBe([("Saved.", politeness)]);
        root.FindAll("[aria-live], [role=status], [role=alert]").ShouldBeEmpty();
        root.FindAll("fluent-toast-b").ShouldBeEmpty();
    }

    [Fact]
    public void AnErrorStaysUntilItIsDismissedByItsNamedButton()
    {
        var root = Render<CmsThemeRoot>();

        Show(root, "Not saved.", CmsToastIntent.Error);
        root.WaitForAssertion(() => root.FindAll(".cms-toast").Count.ShouldBe(1));
        time.Timers.ShouldBe(0);

        var close = root.Find(".cms-toast fluent-button");
        close.GetAttribute("aria-label").ShouldBe("Close");
        close.Click();

        root.WaitForAssertion(() => root.FindAll(".cms-toast").ShouldBeEmpty());
    }

    [Fact]
    public void ASuccessGoesAfterItsLifetimeAndTwoOfTheSameTextAreTwo()
    {
        var root = Render<CmsThemeRoot>();

        Show(root, "Saved.", CmsToastIntent.Success);
        Show(root, "Saved.", CmsToastIntent.Success);
        root.WaitForAssertion(() => root.FindAll(".cms-toast").Count.ShouldBe(2));
        time.DueTimes.ShouldBe([TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(7)]);

        time.Elapse();

        root.WaitForAssertion(() => root.FindAll(".cms-toast").ShouldBeEmpty());
    }

    [Fact]
    public void WithoutAnAnnouncerAToastIsShownOnly()
    {
        using var context = new ComponentTestContextWithoutAnnouncer();
        var root = context.Render<CmsThemeRoot>();

        _ = root.InvokeAsync(() => context.Services.GetRequiredService<ICmsToastService>().ShowAsync("Saved."));

        root.WaitForAssertion(() => root.Find(".cms-toast-message").TextContent.ShouldBe("Saved."));
    }

    [Fact]
    public async Task AnEmptyMessageIsRefused()
    {
        await Should.ThrowAsync<ArgumentException>(() => Services.GetRequiredService<ICmsToastService>().ShowAsync(" "));
    }

    private void Show(IRenderedComponent<CmsThemeRoot> root, string message, CmsToastIntent intent = CmsToastIntent.Info) =>
        _ = root.InvokeAsync(() => Services.GetRequiredService<ICmsToastService>().ShowAsync(message, intent));

    private sealed class ComponentTestContextWithoutAnnouncer : ComponentTestContext;

    private sealed class Announcements : IAnnouncer
    {
        public List<(string, AnnouncementPoliteness)> Messages { get; } = [];

        public void Announce(string message, AnnouncementPoliteness politeness = AnnouncementPoliteness.Polite) => Messages.Add((message, politeness));
    }

    // A clock whose timers run when the test says so.
    private sealed class ManualTime : TimeProvider
    {
        private readonly List<(TimerCallback Callback, object? State, TimeSpan Due)> timers = [];

        public int Timers => timers.Count;

        public IReadOnlyList<TimeSpan> DueTimes => [.. timers.Select(timer => timer.Due)];

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            timers.Add((callback, state, dueTime));
            return new Timer();
        }

        public void Elapse()
        {
            var due = timers.ToArray();
            timers.Clear();
            foreach (var (callback, state, _) in due)
            {
                callback(state);
            }
        }

        private sealed class Timer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
