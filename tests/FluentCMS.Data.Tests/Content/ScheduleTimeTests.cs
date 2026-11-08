using FluentCMS.Content;
using FluentCMS.Core.Content;
using FluentCMS.Publishing;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Content;

public sealed class ScheduleTimeTests
{
    private static readonly DateTimeOffset Now = new(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static DateTime Wall(int year, int month, int day, int hour, int minute = 0) => new(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);

    private static (DateTimeOffset RunAt, string? Zone) Resolve(DateTime at, string zone) =>
        ScheduleTime.Resolve(ScheduleRequest.AtLocalTime(ScheduleAction.Publish, at, zone), Now);

    private static (DateTimeOffset RunAt, string? Zone) Resolve(DateTimeOffset at) =>
        ScheduleTime.Resolve(ScheduleRequest.At(ScheduleAction.Publish, at), Now);

    private static ValidationError Refusal(DateTime at, string zone) =>
        Should.Throw<ValidationException>(() => Resolve(at, zone)).Errors.ShouldHaveSingleItem();

    private static ValidationError Refusal(DateTimeOffset at) =>
        Should.Throw<ValidationException>(() => Resolve(at)).Errors.ShouldHaveSingleItem();

    [Fact]
    public void TheSameWallClockTimeIsADifferentInstantEitherSideOfTheChangeOfClocks()
    {
        Resolve(Wall(2027, 3, 27, 12), "Europe/Berlin").RunAt.ShouldBe(new DateTimeOffset(2027, 3, 27, 11, 0, 0, TimeSpan.Zero));
        Resolve(Wall(2027, 3, 29, 12), "Europe/Berlin").RunAt.ShouldBe(new DateTimeOffset(2027, 3, 29, 10, 0, 0, TimeSpan.Zero));
        Resolve(Wall(2027, 3, 13, 9), "America/New_York").RunAt.ShouldBe(new DateTimeOffset(2027, 3, 13, 14, 0, 0, TimeSpan.Zero));
        Resolve(Wall(2027, 3, 15, 9), "America/New_York").RunAt.ShouldBe(new DateTimeOffset(2027, 3, 15, 13, 0, 0, TimeSpan.Zero));
        Resolve(Wall(2027, 6, 1, 9), "Asia/Kolkata").RunAt.ShouldBe(new DateTimeOffset(2027, 6, 1, 3, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void ATimeTheClocksSkipIsRefusedAndOneTheyRepeatRunsTheFirstTime()
    {
        var skipped = Refusal(Wall(2027, 3, 14, 2, 30), "America/New_York");
        skipped.Code.ShouldBe(ContentErrorCodes.NonexistentLocalTime);
        skipped.Path.ShouldBe("at");

        var repeated = Resolve(Wall(2027, 11, 7, 1, 30), "America/New_York");
        repeated.RunAt.ShouldBe(new DateTimeOffset(2027, 11, 7, 5, 30, 0, TimeSpan.Zero));

        Resolve(Wall(2027, 10, 31, 2, 30), "Europe/Berlin").RunAt.ShouldBe(new DateTimeOffset(2027, 10, 31, 0, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void AnInstantRunsThenWhateverOffsetItIsWrittenWith()
    {
        var (runAt, zone) = Resolve(new DateTimeOffset(2027, 5, 1, 10, 15, 0, TimeSpan.FromHours(2)));

        runAt.ShouldBe(new DateTimeOffset(2027, 5, 1, 8, 15, 0, TimeSpan.Zero));
        runAt.Offset.ShouldBe(TimeSpan.Zero);
        zone.ShouldBeNull();
    }

    [Fact]
    public void TheKindOfALocalTimeIsIgnored()
    {
        var expected = new DateTimeOffset(2027, 5, 1, 6, 0, 0, TimeSpan.Zero);

        Resolve(new DateTime(2027, 5, 1, 8, 0, 0, DateTimeKind.Local), "Europe/Berlin").RunAt.ShouldBe(expected);
        Resolve(new DateTime(2027, 5, 1, 8, 0, 0, DateTimeKind.Utc), "Europe/Berlin").RunAt.ShouldBe(expected);
        Resolve(Wall(2027, 5, 1, 8), "Europe/Berlin").RunAt.ShouldBe(expected);
        ScheduleRequest.AtLocalTime(ScheduleAction.Publish, new DateTime(2027, 5, 1, 8, 0, 0, DateTimeKind.Utc), "Europe/Berlin").LocalTime!.Value.Kind.ShouldBe(DateTimeKind.Unspecified);
    }

    [Fact]
    public void ARequestIsAnInstantOrALocalTimeInAZoneAndNeverBoth()
    {
        var instant = ScheduleRequest.At(ScheduleAction.Unpublish, Now.AddDays(1));
        (instant.Action, instant.Instant, instant.LocalTime, instant.TimeZone).ShouldBe((ScheduleAction.Unpublish, Now.AddDays(1), null, null));

        var local = ScheduleRequest.AtLocalTime(ScheduleAction.Publish, Wall(2027, 5, 1, 8), "Europe/Berlin");
        (local.Action, local.Instant, local.LocalTime, local.TimeZone).ShouldBe((ScheduleAction.Publish, null, Wall(2027, 5, 1, 8), "Europe/Berlin"));

        local.ShouldBe(ScheduleRequest.AtLocalTime(ScheduleAction.Publish, Wall(2027, 5, 1, 8), "Europe/Berlin"));
        Should.Throw<ArgumentNullException>(() => ScheduleRequest.AtLocalTime(ScheduleAction.Publish, Wall(2027, 5, 1, 8), null!));
    }

    [Fact]
    public void AWindowsZoneIdIsRefusedOnEveryPlatform()
    {
        var error = Refusal(Wall(2027, 5, 1, 8), "W. Europe Standard Time");

        error.Path.ShouldBe("timeZone");
        error.Code.ShouldBe(ValidationErrorCodes.InvalidValue);
    }

    [Theory]
    [InlineData("Nowhere/Atlantis")]
    [InlineData("")]
    [InlineData("  ")]
    public void AnUnknownZoneIsRefused(string zone)
    {
        var error = Refusal(Wall(2027, 5, 1, 8), zone);

        error.Path.ShouldBe("timeZone");
        error.Code.ShouldBe(ValidationErrorCodes.InvalidValue);
    }

    [Fact]
    public void AZoneThatIsFarTooLongIsRefusedWithoutBeingLookedUp() =>
        Refusal(Wall(2027, 5, 1, 8), new string('a', 65)).Path.ShouldBe("timeZone");

    [Fact]
    public void ATimeThatIsNotInTheFutureIsRefusedWhateverZoneItIsGivenIn()
    {
        Refusal(Now).Code.ShouldBe(ContentErrorCodes.ScheduleNotInFuture);
        Refusal(new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero)).Code.ShouldBe(ContentErrorCodes.ScheduleNotInFuture);
        Refusal(new DateTimeOffset(2027, 1, 1, 0, 30, 0, TimeSpan.FromHours(1))).Code.ShouldBe(ContentErrorCodes.ScheduleNotInFuture);

        // Midnight in Berlin is 23:00 UTC the day before, which has gone by.
        Refusal(Wall(2027, 1, 1, 0), "Europe/Berlin").Code.ShouldBe(ContentErrorCodes.ScheduleNotInFuture);
        Resolve(Wall(2027, 1, 1, 2), "Europe/Berlin").RunAt.ShouldBe(new DateTimeOffset(2027, 1, 1, 1, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void ATimeAtTheEdgeOfTheCalendarIsRefusedNotThrown()
    {
        Refusal(DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Unspecified), "America/New_York").Code.ShouldBe(ValidationErrorCodes.InvalidValue);
        Refusal(DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Unspecified), "Asia/Kolkata").Path.ShouldBe("at");
        Refusal(DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Unspecified), "America/New_York").Code.ShouldBe(ContentErrorCodes.ScheduleNotInFuture);
        Resolve(DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Unspecified), "Asia/Kolkata").RunAt.Year.ShouldBe(9999);
        Resolve(DateTime.SpecifyKind(DateTime.MaxValue.AddDays(-2), DateTimeKind.Unspecified), "Europe/Berlin").RunAt.Year.ShouldBe(9999);
    }

    [Fact]
    public void AnActionThatIsNotOneOfTheTwoIsRefused() =>
        Should.Throw<ValidationException>(() => ScheduleTime.Resolve(ScheduleRequest.At((ScheduleAction)9, new DateTimeOffset(2027, 5, 1, 8, 0, 0, TimeSpan.Zero)), Now))
            .Errors.ShouldHaveSingleItem().Path.ShouldBe("action");
}
