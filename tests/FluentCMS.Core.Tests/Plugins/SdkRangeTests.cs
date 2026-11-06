using FluentCMS.Core.Plugins;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Plugins;

public class SdkRangeTests
{
    [Theory]
    [InlineData("[1.0.0,2.0.0)", "1.0.0", true)]
    [InlineData("[1.0.0,2.0.0)", "1.9.9", true)]
    [InlineData("[1.0.0,2.0.0)", "2.0.0", false)]
    [InlineData("[1.0.0,2.0.0)", "0.9.0", false)]
    [InlineData("(1.0.0,2.0.0]", "1.0.0", false)]
    [InlineData("(1.0.0,2.0.0]", "2.0.0", true)]
    [InlineData("1.2", "1.2.0", true)]
    [InlineData("1.2", "9.0.0", true)]
    [InlineData("1.2", "1.1.9", false)]
    [InlineData("[1.2.3]", "1.2.3", true)]
    [InlineData("[1.2.3]", "1.2.4", false)]
    [InlineData("(,2.0.0)", "1.0.0", true)]
    [InlineData("(,2.0.0)", "2.0.0", false)]
    [InlineData("[1.0.0,)", "42.0.0", true)]
    [InlineData(" [ 1.0.0 , 2.0.0 ) ", "1.5.0", true)]
    [InlineData("[1.0.0,2.0.0)", "1.0.0+abc", true)]
    public void ARangeHoldsReleasesAsNuGetDoes(string range, string version, bool satisfied) =>
        SdkRange.Parse(range)!.Satisfies(SemanticVersion.Parse(version)!).ShouldBe(satisfied);

    // A pre-release counts as the release it leads to, so a range written for a line takes that line's previews and
    // refuses the next line's, unless a bound names a pre-release itself.
    [Theory]
    [InlineData("[1.0.0,2.0.0)", "1.0.0-preview.1", true)]
    [InlineData("[1.0.0,2.0.0)", "1.0.0-dev", true)]
    [InlineData("[1.0.0,2.0.0)", "1.4.0-rc.2", true)]
    [InlineData("[1.0.0,2.0.0)", "2.0.0-preview.1", false)]
    [InlineData("[1.1.0,2.0.0)", "1.0.9-preview", false)]
    [InlineData("(1.0.0,2.0.0)", "1.0.0-preview", false)]
    [InlineData("[1.0.0]", "1.0.0-preview", true)]
    [InlineData("[1.0.0-preview.2,2.0.0)", "1.0.0-preview.1", false)]
    [InlineData("[1.0.0-preview.2,2.0.0)", "1.0.0-preview.10", true)]
    [InlineData("[1.0.0-preview.2,2.0.0)", "1.0.0", true)]
    [InlineData("[1.0.0,2.0.0-preview.3)", "2.0.0-preview.2", true)]
    [InlineData("[1.0.0,2.0.0-preview.3)", "2.0.0-preview.3", false)]
    public void APrereleaseOfTheSdkCountsAsTheReleaseItLeadsTo(string range, string version, bool satisfied) =>
        SdkRange.Parse(range)!.Satisfies(SemanticVersion.Parse(version)!).ShouldBe(satisfied);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.0.0.0")]
    [InlineData("one")]
    [InlineData("[1.0.0")]
    [InlineData("1.0.0)")]
    [InlineData("(1.0.0)")]
    [InlineData("[1.0.0)")]
    [InlineData("(,)")]
    [InlineData("[2.0.0,1.0.0]")]
    [InlineData("[1.0.0,1.0.0)")]
    [InlineData("[1.0.0,2.0.0,3.0.0]")]
    [InlineData("[01.0.0,2.0.0)")]
    [InlineData("[1.0.0-,2.0.0)")]
    public void AnythingElseIsNotARange(string? range) =>
        SdkRange.Parse(range).ShouldBeNull();

    [Theory]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta")]
    [InlineData("1.0.0-alpha.beta", "1.0.0-beta")]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.11")]
    [InlineData("1.0.0-rc.1", "1.0.0")]
    [InlineData("1.0.0", "1.0.1")]
    [InlineData("1.9.0", "1.10.0")]
    public void VersionsFollowSemVerPrecedence(string lower, string higher) =>
        SemanticVersion.Parse(lower)!.CompareTo(SemanticVersion.Parse(higher)).ShouldBeLessThan(0);

    [Theory]
    [InlineData("1.0")]
    [InlineData("1.0.0.0")]
    [InlineData("v1.0.0")]
    [InlineData("1.0.0-01")]
    [InlineData("1.0.0+")]
    [InlineData(" 1.0.0")]
    public void AStrictVersionHasThreePartsAndValidLabels(string version) =>
        SemanticVersion.Parse(version).ShouldBeNull();
}
