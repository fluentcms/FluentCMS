using FluentCMS.Admin.Configuration;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Configuration;

public sealed class AccentColorsTests
{
    [Theory]
    [InlineData("#ffd400", "#000000", "#ffffff")]
    [InlineData("#00205b", "#ffffff", "#000000")]
    [InlineData("#154ef2", "#ffffff", "#000000")]
    [InlineData("#fff", "#000000", "#ffffff")]
    [InlineData("#ffd400cc", "#000000", "#ffffff")]
    [InlineData("#00205B", "#ffffff", "#000000")]
    public void TheColorOnTheAccentIsBlackOrWhiteWhicheverContrastsMoreAndTheShadeIsTheOther(string accent, string onAccent, string shade) =>
        AccentColors.Of(accent).ShouldBe((onAccent, shade));

    // Every gray from black to white, which spans every luminance an accent can have.
    [Fact]
    public void TheColorOnAnyAccentContrastsWithItByAtLeastFourAndAHalfToOne()
    {
        for (var level = 0; level <= 255; level++)
        {
            var accent = $"#{level:x2}{level:x2}{level:x2}";
            var (onAccent, _) = AccentColors.Of(accent);

            Contrast(accent, onAccent).ShouldBeGreaterThanOrEqualTo(4.5, accent);
        }
    }

    private static double Contrast(string first, string second)
    {
        var (lighter, darker) = (AccentColors.Luminance(first), AccentColors.Luminance(second)) is var (a, b) && a > b ? (a, b) : (b, a);
        return (lighter + 0.05) / (darker + 0.05);
    }
}
