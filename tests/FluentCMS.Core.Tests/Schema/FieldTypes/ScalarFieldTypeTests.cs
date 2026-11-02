using System.Globalization;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Schema.FieldTypes;

public class ScalarFieldTypeTests
{
    [Fact]
    public void IntegerAcceptsWholeNumbersAsLong()
    {
        TestFields.Coerce("integer", 5).ShouldBe(5L);
        TestFields.Coerce("integer", (short)-3).ShouldBe(-3L);
        TestFields.Coerce("integer", long.MaxValue).ShouldBe(long.MaxValue);
        TestFields.Coerce("integer", 3.0m).ShouldBe(3L);
        TestFields.Coerce("integer", 4.0).ShouldBe(4L);
        TestFields.Coerce("integer", "42").ShouldBe(42L);
        TestFields.Coerce("integer", "-7").ShouldBe(-7L);
        TestFields.Coerce("integer", TestFields.Json("5")).ShouldBe(5L);
        TestFields.Coerce("integer", TestFields.Json("5.0")).ShouldBe(5L);
        TestFields.Coerce("integer", TestFields.Json("\"8\"")).ShouldBe(8L);
        TestFields.Coerce("integer", null).ShouldBeNull();
    }

    [Theory]
    [InlineData("3.5")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("1,000")]
    [InlineData("99999999999999999999")]
    public void IntegerRejectsTextThatIsNotAWholeNumber(string text)
    {
        TestFields.Reject("integer", text).ShouldBe("is not a whole number");
    }

    [Fact]
    public void IntegerRejectsOtherValues()
    {
        TestFields.Reject("integer", 3.5).ShouldBe("is not a whole number that fits in 64 bits");
        TestFields.Reject("integer", 1e30).ShouldBe("is not a whole number that fits in 64 bits");
        TestFields.Reject("integer", ulong.MaxValue).ShouldBe("is not a whole number that fits in 64 bits");
        TestFields.Reject("integer", double.NaN).ShouldBe("is not a whole number that fits in 64 bits");
        TestFields.Reject("integer", true).ShouldBe("expected a whole number but got a boolean");
        TestFields.Reject("integer", TestFields.Json("{}")).ShouldBe("expected a whole number but got an object");
    }

    [Fact]
    public void IntegerDoesNotApplyRangeLimits()
    {
        TestFields.Coerce("integer", 500, field => field.Range(1, 10)).ShouldBe(500L);
    }

    [Fact]
    public void DecimalAcceptsNumbersAndNormalisesTrailingZeros()
    {
        TestFields.Coerce("decimal", 12.5m).ShouldBe(12.5m);
        TestFields.Coerce("decimal", 12).ShouldBe(12m);
        TestFields.Coerce("decimal", 1.5).ShouldBe(1.5m);
        TestFields.Coerce("decimal", 1.5f).ShouldBe(1.5m);
        TestFields.Coerce("decimal", "12.50").ShouldBe(12.5m);
        TestFields.Coerce("decimal", "-0.25").ShouldBe(-0.25m);
        TestFields.Coerce("decimal", "1e3").ShouldBe(1000m);
        TestFields.Coerce("decimal", TestFields.Json("1.25")).ShouldBe(1.25m);
        TestFields.Coerce("decimal", TestFields.Json("7")).ShouldBe(7m);
        TestFields.Coerce("decimal", null).ShouldBeNull();
        ((decimal)TestFields.Coerce("decimal", 12.50m)!).ToString(CultureInfo.InvariantCulture).ShouldBe("12.5");
    }

    [Fact]
    public void DecimalRejectsOtherValues()
    {
        TestFields.Reject("decimal", "abc").ShouldBe("is not a number");
        TestFields.Reject("decimal", "1,5").ShouldBe("is not a number");
        TestFields.Reject("decimal", "").ShouldBe("is not a number");
        TestFields.Reject("decimal", double.PositiveInfinity).ShouldBe("is not a number that fits in a decimal");
        TestFields.Reject("decimal", 1e30).ShouldBe("is not a number that fits in a decimal");
        TestFields.Reject("decimal", false).ShouldBe("expected a number but got a boolean");
    }

    [Fact]
    public void DecimalHonoursTheScale()
    {
        TestFields.Coerce("decimal", 1.23m, field => field.Setting("scale", 2)).ShouldBe(1.23m);
        TestFields.Coerce("decimal", "1.20", field => field.Setting("scale", 2)).ShouldBe(1.2m);
        TestFields.Reject("decimal", 1.234m, field => field.Setting("scale", 2)).ShouldBe("has 3 decimal places, more than the 2 the field allows");
    }

    [Fact]
    public void DecimalHonoursThePrecisionAndScaleTogether()
    {
        void Configure(FluentCMS.Schema.IFieldBuilder field) => field.Setting("precision", 5).Setting("scale", 2);

        TestFields.Coerce("decimal", 123.45m, Configure).ShouldBe(123.45m);
        TestFields.Coerce("decimal", -999.99m, Configure).ShouldBe(-999.99m);
        TestFields.Coerce("decimal", 0.5m, Configure).ShouldBe(0.5m);
        TestFields.Reject("decimal", 1234.5m, Configure).ShouldBe("has more than the 5 significant digits the field allows");
    }

    [Fact]
    public void DecimalWithOnlyAPrecisionLimitsTheTotalDigits()
    {
        void Configure(FluentCMS.Schema.IFieldBuilder field) => field.Setting("precision", 4);

        TestFields.Coerce("decimal", 1234m, Configure).ShouldBe(1234m);
        TestFields.Coerce("decimal", 123.4m, Configure).ShouldBe(123.4m);
        TestFields.Reject("decimal", 12345m, Configure).ShouldBe("has more than the 4 significant digits the field allows");
        TestFields.Reject("decimal", 123.45m, Configure).ShouldBe("has more than the 4 significant digits the field allows");
    }

    [Fact]
    public void DecimalParsingIgnoresTheCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            TestFields.Coerce("decimal", "1.5").ShouldBe(1.5m);
            TestFields.Reject("decimal", "1,5").ShouldBe("is not a number");
            TestFields.Coerce("integer", "-12").ShouldBe(-12L);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void BooleanAcceptsBooleansAndTheirText()
    {
        TestFields.Coerce("boolean", true).ShouldBe(true);
        TestFields.Coerce("boolean", false).ShouldBe(false);
        TestFields.Coerce("boolean", "TRUE").ShouldBe(true);
        TestFields.Coerce("boolean", "false").ShouldBe(false);
        TestFields.Coerce("boolean", TestFields.Json("true")).ShouldBe(true);
        TestFields.Coerce("boolean", TestFields.Json("false")).ShouldBe(false);
        TestFields.Coerce("boolean", null).ShouldBeNull();
    }

    [Fact]
    public void BooleanRejectsOtherValues()
    {
        TestFields.Reject("boolean", "yes").ShouldBe("is not true or false");
        TestFields.Reject("boolean", "").ShouldBe("is not true or false");
        TestFields.Reject("boolean", 1).ShouldBe("expected a boolean but got a number");
        TestFields.Reject("boolean", TestFields.Json("0")).ShouldBe("expected a boolean but got a number");
    }

    [Fact]
    public void DateAcceptsIsoDates()
    {
        var day = new DateOnly(2026, 11, 1);

        TestFields.Coerce("date", day).ShouldBe(day);
        TestFields.Coerce("date", "2026-11-01").ShouldBe(day);
        TestFields.Coerce("date", TestFields.Json("\"2026-11-01\"")).ShouldBe(day);
        TestFields.Coerce("date", new DateTime(2026, 11, 1)).ShouldBe(day);
        TestFields.Coerce("date", null).ShouldBeNull();
    }

    [Theory]
    [InlineData("2026-13-01")]
    [InlineData("2026-02-30")]
    [InlineData("11/01/2026")]
    [InlineData("1 November 2026")]
    [InlineData("2026-11-01T10:00:00Z")]
    [InlineData("")]
    public void DateRejectsOtherText(string text)
    {
        TestFields.Reject("date", text).ShouldBe("is not a date in the form yyyy-MM-dd");
    }

    [Fact]
    public void DateRejectsOtherValues()
    {
        TestFields.Reject("date", new DateTime(2026, 11, 1, 10, 0, 0)).ShouldBe("has a time of day, but the field holds a date only");
        TestFields.Reject("date", 20261101).ShouldBe("expected a date but got a number");
    }

    [Theory]
    [InlineData("2026-11-01T09:12:00Z", "2026-11-01T09:12:00Z")]
    [InlineData("2026-11-01T10:12:00+01:00", "2026-11-01T09:12:00Z")]
    [InlineData("2026-11-01T04:12:00-05:00", "2026-11-01T09:12:00Z")]
    [InlineData("2026-11-01T09:12:00", "2026-11-01T09:12:00Z")]
    [InlineData("2026-11-01T09:12", "2026-11-01T09:12:00Z")]
    [InlineData("2026-11-01T09:12Z", "2026-11-01T09:12:00Z")]
    [InlineData("2026-11-01T09:12:00.1234567Z", "2026-11-01T09:12:00.1234567Z")]
    [InlineData("2026-11-01T09:12:00.5+02:00", "2026-11-01T07:12:00.5Z")]
    public void DateTimeAcceptsIsoTextAndKeepsAUtcInstant(string text, string expected)
    {
        var result = TestFields.Coerce("dateTime", text).ShouldBeOfType<DateTimeOffset>();

        result.ShouldBe(DateTimeOffset.Parse(expected, CultureInfo.InvariantCulture));
        result.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void DateTimeAcceptsTypedValuesAndJson()
    {
        var instant = new DateTimeOffset(2026, 11, 1, 9, 12, 0, TimeSpan.Zero);

        TestFields.Coerce("dateTime", instant).ShouldBe(instant);
        TestFields.Coerce("dateTime", new DateTimeOffset(2026, 11, 1, 10, 12, 0, TimeSpan.FromHours(1))).ShouldBe(instant);
        TestFields.Coerce("dateTime", new DateTime(2026, 11, 1, 9, 12, 0, DateTimeKind.Utc)).ShouldBe(instant);
        TestFields.Coerce("dateTime", new DateTime(2026, 11, 1, 9, 12, 0, DateTimeKind.Unspecified)).ShouldBe(instant);
        TestFields.Coerce("dateTime", TestFields.Json("\"2026-11-01T09:12:00Z\"")).ShouldBe(instant);
        TestFields.Coerce("dateTime", null).ShouldBeNull();
    }

    [Theory]
    [InlineData("2026-11-01")]
    [InlineData("yesterday")]
    [InlineData("2026-11-01 09:12:00")]
    [InlineData("11/01/2026 09:12")]
    [InlineData("")]
    public void DateTimeRejectsOtherText(string text)
    {
        TestFields.Reject("dateTime", text).ShouldBe("is not an ISO 8601 date and time such as 2026-11-01T09:12:00Z");
    }

    [Fact]
    public void DateTimeRejectsOtherValues()
    {
        TestFields.Reject("dateTime", 1762000000).ShouldBe("expected a date and time but got a number");
        TestFields.Reject("dateTime", new DateOnly(2026, 11, 1)).ShouldBe("expected a date and time but got a value of type DateOnly");
    }

    [Fact]
    public void DateTimeParsingIgnoresTheCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");

            TestFields.Coerce("dateTime", "2026-11-01T09:12:00Z").ShouldBe(new DateTimeOffset(2026, 11, 1, 9, 12, 0, TimeSpan.Zero));
            TestFields.Coerce("date", "2026-11-01").ShouldBe(new DateOnly(2026, 11, 1));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
