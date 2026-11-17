using FluentCMS.Admin.Plugins;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Plugins;

// The range of the contract an admin half was built for, in NuGet's notation.
public sealed class AdminVersionRangeTests
{
    [Theory]
    [InlineData("[1.0.0,2.0.0)", "1.0.0", true)]
    [InlineData("[1.0.0,2.0.0)", "1.9.3", true)]
    [InlineData("[1.0.0,2.0.0)", "2.0.0", false)]
    [InlineData("[1.0.0,2.0.0)", "0.9.0", false)]
    [InlineData("(1.0.0,2.0.0]", "1.0.0", false)]
    [InlineData("(1.0.0,2.0.0]", "2.0.0", true)]
    [InlineData("(,2.0.0]", "0.1.0", true)]
    [InlineData("[1.2.0,)", "3.0.0", true)]
    [InlineData("[1.2.0]", "1.2.0", true)]
    [InlineData("[1.2.0]", "1.2.1", false)]
    [InlineData("1.0.0", "4.0.0", true)]
    [InlineData("1.0.0", "0.9.0", false)]
    [InlineData(" [1.0, 2) ", "1.0.0.5", true)]
    [InlineData("[1.0.0-preview.1,2.0.0)", "1.0.0", true)]
    public void ARangeHoldsTheVersionsOfItsNotation(string text, string version, bool held)
    {
        AdminVersionRange.TryParse(text, out var range).ShouldBeTrue();

        range.ShouldNotBeNull().Contains(Version.Parse(version)).ShouldBe(held);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("soon")]
    [InlineData("[1.0.0,2.0.0")]
    [InlineData("[1.0.0,2.0.0,3.0.0)")]
    [InlineData("(1.0.0)")]
    [InlineData("[,]")]
    [InlineData("[2.0.0,1.0.0]")]
    [InlineData("[one,2.0.0)")]
    [InlineData("[]")]
    public void ATextThatIsNoRangeIsRefused(string? text)
    {
        AdminVersionRange.TryParse(text, out var range).ShouldBeFalse();
        range.ShouldBeNull();
    }
}
