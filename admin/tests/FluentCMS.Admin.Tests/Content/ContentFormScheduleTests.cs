using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using FluentCMS.Admin.Components;
using FluentCMS.Admin.Content;
using FluentCMS.Admin.Localization;
using FluentCMS.Admin.Tests.Shell;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Content;

// The schedules of the form (A-27): a publish or an unpublish at a time entered in the user's time zone, which the dialog
// names and sends with the wall-clock time; the pending schedules as badges with move and cancel; the server's refusals in
// its own words; and no controls for a caller who may not publish.
public sealed class ContentFormScheduleTests : AdminTestContext
{
    private const string Berlin = "Europe/Berlin";

    // UTC+14, the zone farthest ahead of UTC.
    private const string Kiritimati = "Pacific/Kiritimati";

    private readonly FakeContent _content;

    public ContentFormScheduleTests()
    {
        UseNeutralResources();
        _content = new FakeContent(Management);
        Management.SetTypes(MainSite, Post());
    }

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    private FormatService Formats => Services.GetRequiredService<FormatService>();

    [Fact]
    public void AnEditorSchedulesAPublishAtAWallClockTimeOfTheirZoneAndSeesIt()
    {
        Management.Preferences["timeZone"] = Berlin;
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Load($"content/post/{id:D}");

        ScheduleButton(app, "Schedule").Click();
        var dialog = Dialog(app, "Schedule a publish or an unpublish");
        app.WaitForAssertion(() => dialog.Instance.Open.ShouldBeTrue());
        dialog.Find(".content-schedule-zone").TextContent.ShouldBe("Time zone: Europe/Berlin");
        dialog.Markup.ShouldContain("In the locale en.");
        Enter(dialog, new DateOnly(2030, 7, 1), new TimeOnly(9, 0));
        DialogButton(dialog, "Schedule").Click();

        // 09:00 in Berlin's summer is 07:00 UTC, which the server works out from the zone.
        app.WaitForAssertion(() => _content.Scheduled(id, "publish").ShouldBe(new DateTimeOffset(2030, 7, 1, 7, 0, 0, TimeSpan.Zero)));
        var write = _content.Writes.ShouldHaveSingleItem();
        (write.Method, write.Path).ShouldBe((HttpMethod.Post, $"content/post/{id:D}/schedule"));
        write.Body.ToJsonString().ShouldBe("""{"action":"publish","at":"2030-07-01T09:00:00","timeZone":"Europe/Berlin"}""");
        app.WaitForAssertion(() => dialog.Instance.Open.ShouldBeFalse());
        var at = new DateTimeOffset(2030, 7, 1, 7, 0, 0, TimeSpan.Zero);
        app.WaitForAssertion(() => ScheduleBadges(app).ShouldBe([Normalize($"en Publishes {Formats.DateTime(at)} Europe/Berlin")]));
        Formats.Local(at).ShouldBe(new DateTime(2030, 7, 1, 9, 0, 0));
        ScheduleControls(app).ShouldBe(["Schedule", "Move the publish", "Cancel the publish"]);
        Announced(app).Select(Normalize).ShouldContain(Normalize($"Publish scheduled for {Formats.DateTime(at)}, Europe/Berlin."));
    }

    // A schedule set from Berlin is shown to a user in Kiritimati in their own time, and moved there in it.
    [Fact]
    public void APendingScheduleIsShownAndMovedInTheZoneOfTheUserWhoSeesIt()
    {
        Management.Preferences["timeZone"] = Kiritimati;
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" }, status: "published");
        var at = new DateTimeOffset(2030, 1, 15, 20, 0, 0, TimeSpan.Zero);
        _content.Schedule(id, "unpublish", at);
        var app = Load($"content/post/{id:D}");

        ScheduleBadges(app).ShouldBe([Normalize($"en Unpublishes {Formats.DateTime(at)} Pacific/Kiritimati")]);
        Formats.Local(at).ShouldBe(new DateTime(2030, 1, 16, 10, 0, 0));

        ScheduleButton(app, "Move the unpublish").Click();
        var dialog = Dialog(app, "Move the scheduled unpublish");
        app.WaitForAssertion(() => dialog.Instance.Open.ShouldBeTrue());
        dialog.FindComponent<CmsDatePicker>().Instance.Value.ShouldBe(new DateOnly(2030, 1, 16));
        dialog.FindComponent<CmsTimePicker>().Instance.Value.ShouldBe(new TimeOnly(10, 0));
        Enter(dialog, new DateOnly(2030, 1, 16), new TimeOnly(11, 0));
        DialogButton(dialog, "Move").Click();

        app.WaitForAssertion(() => _content.Scheduled(id, "unpublish").ShouldBe(at.AddHours(1)));
        _content.Writes.ShouldHaveSingleItem().Body.ToJsonString().ShouldBe("""{"action":"unpublish","at":"2030-01-16T11:00:00","timeZone":"Pacific/Kiritimati"}""");
        app.WaitForAssertion(() => ScheduleBadges(app).ShouldBe([Normalize($"en Unpublishes {Formats.DateTime(at.AddHours(1))} Pacific/Kiritimati")]));
    }

    [Theory]
    [InlineData(2000, 1, 1, 9, "The time is not in the future.")]
    [InlineData(2030, 3, 31, 2, "That time does not exist in Europe/Berlin: the clocks skip it.")]
    public void ATimeThePastHoldsOrTheClocksSkipIsRefusedInTheServersWords(int year, int month, int day, int hour, string message)
    {
        Management.Preferences["timeZone"] = Berlin;
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Load($"content/post/{id:D}");

        ScheduleButton(app, "Schedule").Click();
        var dialog = Dialog(app, "Schedule a publish or an unpublish");
        app.WaitForAssertion(() => dialog.Instance.Open.ShouldBeTrue());
        Enter(dialog, new DateOnly(year, month, day), new TimeOnly(hour, 30));
        DialogButton(dialog, "Schedule").Click();

        app.WaitForAssertion(() => dialog.Find(".content-schedule-problem[role=alert]").TextContent.ShouldBe(message));
        dialog.Instance.Open.ShouldBeTrue();
        _content.Scheduled(id, "publish").ShouldBeNull();
        ScheduleBadges(app).ShouldBeEmpty();
    }

    [Fact]
    public void CancelRemovesThePendingScheduleOfTheFormsLocale()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        _content.Schedule(id, "publish", new DateTimeOffset(2030, 5, 1, 8, 0, 0, TimeSpan.Zero));
        var app = Load($"content/post/{id:D}");

        ScheduleButton(app, "Cancel the publish").Click();

        app.WaitForAssertion(() => ScheduleBadges(app).ShouldBeEmpty());
        var cancel = _content.Writes.ShouldHaveSingleItem();
        (cancel.Method, cancel.Path).ShouldBe((HttpMethod.Delete, $"content/post/{id:D}/schedule/publish"));
        _content.Scheduled(id, "publish").ShouldBeNull();
        ScheduleControls(app).ShouldBe(["Schedule"]);
        Announced(app).ShouldContain("The scheduled publish was cancelled.");
    }

    [Fact]
    public void ACallerWhoMayNotPublishSeesTheScheduleAndCannotChangeIt()
    {
        Management.Access[MainSite]["types"]!["post"]!["publish"] = false;
        Management.Access[MainSite]["types"]!["post"]!["unpublish"] = false;
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var at = new DateTimeOffset(2030, 5, 1, 8, 0, 0, TimeSpan.Zero);
        _content.Schedule(id, "publish", at);

        var app = Load($"content/post/{id:D}");

        ScheduleBadges(app).ShouldBe([Normalize($"en Publishes {Formats.DateTime(at)} {EntrySchedule.ZoneId(TimeZoneInfo.Utc)}")]);
        app.FindAll(".content-schedule").ShouldBeEmpty();
    }

    // Unpublish alone: the dialog offers no choice of action, and a pending publish has no controls.
    [Fact]
    public void ACallerWhoMayOnlyUnpublishSchedulesAnUnpublishAndLeavesAPendingPublishAlone()
    {
        Management.Access[MainSite]["types"]!["post"]!["publish"] = false;
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        _content.Schedule(id, "publish", new DateTimeOffset(2030, 5, 1, 8, 0, 0, TimeSpan.Zero));
        var app = Load($"content/post/{id:D}");

        ScheduleControls(app).ShouldBe(["Schedule"]);
        ScheduleButton(app, "Schedule").Click();
        var dialog = Dialog(app, "Schedule a publish or an unpublish");
        app.WaitForAssertion(() => dialog.Instance.Open.ShouldBeTrue());
        dialog.FindComponents<CmsSelect>().ShouldBeEmpty();
        Enter(dialog, new DateOnly(2030, 6, 1), new TimeOnly(8, 0));
        DialogButton(dialog, "Schedule").Click();

        app.WaitForAssertion(() => _content.Scheduled(id, "unpublish").ShouldNotBeNull());
        _content.Writes.ShouldHaveSingleItem().Body["action"]!.GetValue<string>().ShouldBe("unpublish");
    }

    [Fact]
    public void ANewEntryHasNoScheduleControls()
    {
        var app = Load("content/post/new");

        app.FindAll(".content-schedule").ShouldBeEmpty();
    }

    // The server takes an IANA id only; a zone a desktop found by its Windows id goes by its IANA one.
    [Fact]
    public void AZoneIsSentByItsIanaId()
    {
        EntrySchedule.ZoneId(TimeZoneInfo.FindSystemTimeZoneById(Berlin)).ShouldBe(Berlin);
        EntrySchedule.ZoneId(TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time")).ShouldBe(Berlin);
    }

    private static void Enter(IRenderedComponent<CmsDialog> dialog, DateOnly day, TimeOnly time)
    {
        var date = dialog.FindComponent<CmsDatePicker>();
        date.InvokeAsync(() => date.Instance.ValueChanged.InvokeAsync(day)).GetAwaiter().GetResult();
        var clock = dialog.FindComponent<CmsTimePicker>();
        clock.InvokeAsync(() => clock.Instance.ValueChanged.InvokeAsync(time)).GetAwaiter().GetResult();
    }

    private IRenderedComponent<AdminApp> Load(string route)
    {
        Start();
        Navigation.NavigateTo(route);
        var app = Render<AdminApp>();
        app.WaitForAssertion(() => app.FindAll(".content-form p[role='status']").ShouldBeEmpty());
        app.WaitForAssertion(() => app.Find("main h1"));
        return app;
    }

    private static string[] ScheduleControls(IRenderedComponent<AdminApp> app) =>
        [.. app.FindAll(".content-schedule fluent-button").Select(button => button.TextContent.Trim())];

    private static IElement ScheduleButton(IRenderedComponent<AdminApp> app, string text) =>
        app.FindAll(".content-schedule fluent-button").Single(button => button.TextContent.Trim() == text);

    private static string[] ScheduleBadges(IRenderedComponent<AdminApp> app) =>
        [.. app.FindAll(".content-form-toolbar .content-badge-schedule").Select(badge => Normalize(badge.TextContent))];

    private static string Normalize(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static List<string> Announced(IRenderedComponent<AdminApp> app) =>
        [.. app.FindAll("[role=status] > div").Select(message => message.TextContent)];

    private static IRenderedComponent<CmsDialog> Dialog(IRenderedComponent<AdminApp> app, string title) =>
        app.FindComponents<CmsDialog>().Single(found => found.Instance.Title == title);

    private static IElement DialogButton(IRenderedComponent<CmsDialog> dialog, string text) =>
        dialog.FindAll("fluent-button").Single(button => button.TextContent.Trim() == text);

    private static JsonObject Post()
    {
        var type = Type("post", fields: [Field("title", required: true)]);
        type["localized"] = true;
        return type;
    }
}
