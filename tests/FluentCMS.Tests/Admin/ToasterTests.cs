using Bunit;
using FluentCMS.Admin.Components.Ui;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Tests.Admin;

public sealed class ToasterTests
{
    [Fact]
    public void The_live_region_is_there_before_the_first_toast()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<Toaster>();
        var region = cut.Find("[data-slot=toaster]");

        Assert.Equal("region", region.GetAttribute("role"));
        Assert.Equal("Notifications", region.GetAttribute("aria-label"));
        Assert.Equal("Alt+T", region.GetAttribute("aria-keyshortcuts"));
        Assert.Equal("-1", region.GetAttribute("tabindex"));
        Assert.Equal("polite", cut.Find("ol").GetAttribute("aria-live"));
        Assert.Empty(cut.FindAll("li"));
        Assert.Contains("end-4", region.ClassList);
        Assert.DoesNotContain(cut.FindAll("[class]").SelectMany(element => element.ClassList), Utilities.IsPhysical);
    }

    [Fact]
    public void A_toast_shows_its_title_description_and_a_named_close_button_without_taking_focus()
    {
        using var context = new AdminTestContext();
        var toasts = context.Services.GetRequiredService<ToastService>();
        var cut = context.Render<Toaster>();

        toasts.Show("Saved", "The item is in review.");

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("li")));
        var toast = cut.Find("li");
        Assert.Equal("default", toast.GetAttribute("data-variant"));
        Assert.Equal("Saved", cut.Find("[data-slot=toast-title]").TextContent);
        Assert.Equal("The item is in review.", cut.Find("[data-slot=toast-description]").TextContent);
        Assert.Equal("Close", cut.Find("[data-slot=toast-close] .sr-only").TextContent);
        Assert.False(toast.HasAttribute("tabindex"));
        Assert.False(toast.HasAttribute("autofocus"));
    }

    [Fact]
    public void A_destructive_toast_reads_in_the_destructive_colour_and_may_have_no_description()
    {
        using var context = new AdminTestContext();
        var toasts = context.Services.GetRequiredService<ToastService>();
        var cut = context.Render<Toaster>();

        toasts.Show("Could not publish", variant: "destructive");

        cut.WaitForAssertion(() => Assert.Contains("text-destructive", cut.Find("li").ClassList));
        Assert.DoesNotContain("text-popover-foreground", cut.Find("li").ClassList);
        Assert.Empty(cut.FindAll("[data-slot=toast-description]"));
    }

    [Fact]
    public async Task The_close_button_removes_the_toast()
    {
        using var context = new AdminTestContext();
        var toasts = context.Services.GetRequiredService<ToastService>();
        var cut = context.Render<Toaster>();
        toasts.Show("First");
        toasts.Show("Second");
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("li").Count));

        await cut.FindAll("[data-slot=toast-close]")[0].ClickAsync(new());

        Assert.Equal(["Second"], cut.FindAll("[data-slot=toast-title]").Select(title => title.TextContent));
        Assert.Equal(["Second"], toasts.Toasts.Select(toast => toast.Title));
    }

    [Fact]
    public void A_toast_goes_when_its_time_is_up()
    {
        using var context = new AdminTestContext();
        var clock = new ManualTimers();
        context.Services.AddSingleton<TimeProvider>(clock);
        var toasts = context.Services.GetRequiredService<ToastService>();
        var cut = context.Render<Toaster>();

        toasts.Show("Saved");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("li")));
        Assert.Equal([ToastService.Duration], clock.DueTimes);

        clock.FireAll();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("li")));
        Assert.Empty(toasts.Toasts);
    }

    // Records the timers Task.Delay asks for and fires them on demand.
    private sealed class ManualTimers : TimeProvider
    {
        private readonly List<(TimerCallback Callback, object? State)> _timers = [];

        public List<TimeSpan> DueTimes { get; } = [];

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            lock (_timers)
            {
                _timers.Add((callback, state));
                DueTimes.Add(dueTime);
            }
            return new Timer();
        }

        public void FireAll()
        {
            List<(TimerCallback Callback, object? State)> timers;
            lock (_timers)
            {
                timers = [.. _timers];
                _timers.Clear();
            }
            timers.ForEach(timer => timer.Callback(timer.State));
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
