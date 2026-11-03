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

    private static (DateTimeOffset RunAt, string? Zone) Resolve(DateTime at, string? zone) =>
        ScheduleTime.Resolve(new ScheduleRequest(ScheduleAction.Publish, at, zone), Now);

    private static ValidationError Refusal(DateTime at, string? zone) =>
        Should.Throw<ValidationException>(() => Resolve(at, zone)).Errors.ShouldHaveSingleItem();

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
    public void ATimeWithoutAZoneIsAnInstantInUtc()
    {
        var at = new DateTime(2027, 5, 1, 8, 15, 0, DateTimeKind.Utc);

        var (runAt, zone) = Resolve(at, null);

        runAt.ShouldBe(new DateTimeOffset(2027, 5, 1, 8, 15, 0, TimeSpan.Zero));
        zone.ShouldBeNull();
    }

    [Fact]
    public void AKindThatMeansDifferentThingsOnDifferentMachinesIsRefused()
    {
        Refusal(new DateTime(2027, 5, 1, 8, 0, 0, DateTimeKind.Local), null).Code.ShouldBe(ValidationErrorCodes.InvalidValue);
        Refusal(Wall(2027, 5, 1, 8), null).Code.ShouldBe(ValidationErrorCodes.InvalidValue);
        Refusal(new DateTime(2027, 5, 1, 8, 0, 0, DateTimeKind.Utc), "Europe/Berlin").Code.ShouldBe(ValidationErrorCodes.InvalidValue);
        Refusal(new DateTime(2027, 5, 1, 8, 0, 0, DateTimeKind.Local), "Europe/Berlin").Code.ShouldBe(ValidationErrorCodes.InvalidValue);
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
        Refusal(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), null).Code.ShouldBe(ContentErrorCodes.ScheduleNotInFuture);
        Refusal(new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc), null).Code.ShouldBe(ContentErrorCodes.ScheduleNotInFuture);

        // Midnight in Berlin is 23:00 UTC the day before, which has gone by.
        Refusal(Wall(2027, 1, 1, 0), "Europe/Berlin").Code.ShouldBe(ContentErrorCodes.ScheduleNotInFuture);
        Resolve(Wall(2027, 1, 1, 2), "Europe/Berlin").RunAt.ShouldBe(new DateTimeOffset(2027, 1, 1, 1, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void AnActionThatIsNotOneOfTheTwoIsRefused() =>
        Should.Throw<ValidationException>(() => ScheduleTime.Resolve(new ScheduleRequest((ScheduleAction)9, new DateTime(2027, 5, 1, 8, 0, 0, DateTimeKind.Utc)), Now))
            .Errors.ShouldHaveSingleItem().Path.ShouldBe("action");
}
