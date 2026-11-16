using System.Globalization;
using FluentCMS.Admin.Localization;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests;

public sealed class FormatServiceTests
{
    private static readonly TimeZoneInfo PlusTwo = TimeZoneInfo.CreateCustomTimeZone("plus-two", TimeSpan.FromHours(2), "plus-two", "plus-two");
    private static readonly DateTimeOffset LateEvening = new(2026, 11, 15, 23, 30, 0, TimeSpan.Zero);

    [Fact]
    public void DatesAndTimesAreInTheUsersTimeZone()
    {
        var formats = new FormatService(CultureInfo.GetCultureInfo("en-US"), PlusTwo);

        formats.Date(LateEvening).ShouldBe(new DateTime(2026, 11, 16).ToString("d", formats.Culture));
        formats.Time(LateEvening).ShouldBe(new DateTime(2026, 11, 16, 1, 30, 0).ToString("t", formats.Culture));
        formats.DateTime(LateEvening).ShouldBe(new DateTime(2026, 11, 16, 1, 30, 0).ToString("g", formats.Culture));
    }

    [Fact]
    public void DatesFollowTheUsersCulture()
    {
        new FormatService(CultureInfo.GetCultureInfo("en-US"), TimeZoneInfo.Utc).Date(LateEvening).ShouldBe("11/15/2026");
        new FormatService(CultureInfo.GetCultureInfo("de-DE"), TimeZoneInfo.Utc).Date(LateEvening).ShouldBe("15.11.2026");
    }

    [Fact]
    public void NumbersFollowTheUsersCulture()
    {
        var english = new FormatService(CultureInfo.GetCultureInfo("en-US"), TimeZoneInfo.Utc);
        var german = new FormatService(CultureInfo.GetCultureInfo("de-DE"), TimeZoneInfo.Utc);

        english.Number(1234567.5m).ShouldBe("1,234,567.5");
        german.Number(1234567.5m).ShouldBe("1.234.567,5");
        english.Number(3m).ShouldBe("3");
        english.Number(2.5m, 2).ShouldBe("2.50");
    }
}
