using FluentCMS.Sites;
using Shouldly;
using Xunit;

namespace FluentCMS.Sdk.Tests.Sites;

public class SiteIdTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-0000000000b2");

    [Fact]
    public void EqualWhenValuesAreEqual()
    {
        var a = new SiteId(A);

        a.Equals(new SiteId(A)).ShouldBeTrue();
        (a == new SiteId(A)).ShouldBeTrue();
        (a != new SiteId(B)).ShouldBeTrue();
        a.GetHashCode().ShouldBe(new SiteId(A).GetHashCode());
    }

    [Fact]
    public void WithExpressionCreatesNewValue()
    {
        var original = new SiteId(A);

        var changed = original with { Value = B };

        changed.Value.ShouldBe(B);
        original.Value.ShouldBe(A);
    }

    [Fact]
    public void DefaultIsEmpty()
    {
        default(SiteId).ShouldBe(SiteId.Empty);
        SiteId.Empty.IsEmpty.ShouldBeTrue();
        new SiteId(A).IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public void ToStringIsTheGuid()
    {
        new SiteId(A).ToString().ShouldBe(A.ToString());
    }
}
