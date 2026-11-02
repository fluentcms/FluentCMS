using FluentCMS.Content;
using Shouldly;
using Xunit;

namespace FluentCMS.Sdk.Tests.Content;

public class PrincipalTests
{
    private static readonly Guid UserId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    [Fact]
    public void SystemPrincipalIsASingleSystemInstance()
    {
        Principal.System.Kind.ShouldBe(PrincipalKind.System);
        Principal.System.IsSystem.ShouldBeTrue();
        Principal.System.Id.ShouldBe(Guid.Empty);
        Principal.System.ShouldBeSameAs(Principal.System);
        Principal.System.Claims.ShouldBeEmpty();
    }

    [Fact]
    public void OtherKindsAreNotSystem()
    {
        new Principal(PrincipalKind.ServiceAccount, UserId, "ci").IsSystem.ShouldBeFalse();
    }

    [Fact]
    public void ConstructorRejectsTheSystemKind()
    {
        Should.Throw<ArgumentException>(() => new Principal(PrincipalKind.System, UserId, "fake"));
    }

    [Fact]
    public void ConstructorRejectsUnknownKindsAndNullNames()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new Principal(default, UserId, "x"));
        Should.Throw<ArgumentNullException>(() => new Principal(PrincipalKind.Member, UserId, null!));
    }

    [Fact]
    public void AnonymousIsASinglePrincipalWithNoIdentity()
    {
        Principal.Anonymous.Kind.ShouldBe(PrincipalKind.Anonymous);
        Principal.Anonymous.IsAnonymous.ShouldBeTrue();
        Principal.Anonymous.IsSystem.ShouldBeFalse();
        Principal.Anonymous.Id.ShouldBe(Guid.Empty);
        Principal.Anonymous.Claims.ShouldBeEmpty();
        Principal.Anonymous.ShouldNotBe(Principal.System);
        Principal.System.IsAnonymous.ShouldBeFalse();
    }

    [Fact]
    public void AnAnonymousPrincipalWithClaimsCannotBeMade()
    {
        var claims = new Dictionary<string, string> { ["scope"] = "management" };

        Should.Throw<ArgumentException>(() => new Principal(PrincipalKind.Anonymous, UserId, "fake", claims));
    }

    [Theory]
    [InlineData(PrincipalKind.AdminUser)]
    [InlineData(PrincipalKind.Member)]
    [InlineData(PrincipalKind.ApiKey)]
    [InlineData(PrincipalKind.ServiceAccount)]
    public void CarriesKindIdAndName(PrincipalKind kind)
    {
        var principal = new Principal(kind, UserId, "Amir");

        principal.Kind.ShouldBe(kind);
        principal.Id.ShouldBe(UserId);
        principal.Name.ShouldBe("Amir");
    }

    [Fact]
    public void EqualWhenKindAndIdMatch()
    {
        var a = new Principal(PrincipalKind.AdminUser, UserId, "Amir", new Dictionary<string, string> { ["region"] = "eu" });
        var b = new Principal(PrincipalKind.AdminUser, UserId, "Renamed");

        a.Equals(b).ShouldBeTrue();
        a.Equals((object)b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void NotEqualWhenKindOrIdDiffer()
    {
        var user = new Principal(PrincipalKind.AdminUser, UserId, "Amir");

        user.Equals(new Principal(PrincipalKind.Member, UserId, "Amir")).ShouldBeFalse();
        user.Equals(new Principal(PrincipalKind.AdminUser, Guid.Parse("00000000-0000-0000-0000-000000000002"), "Amir")).ShouldBeFalse();
        user.Equals(null).ShouldBeFalse();
        user.Equals(Principal.System).ShouldBeFalse();
    }

    [Fact]
    public void ClaimReturnsTheValueOrNull()
    {
        var principal = new Principal(
            PrincipalKind.Member,
            UserId,
            "Sam",
            new Dictionary<string, string> { ["region"] = "eu" });

        principal.Claim("region").ShouldBe("eu");
        principal.Claim("Region").ShouldBeNull();
        principal.Claim("missing").ShouldBeNull();
    }

    [Fact]
    public void ClaimsAreCopiedOnConstruction()
    {
        var source = new Dictionary<string, string> { ["region"] = "eu" };
        var principal = new Principal(PrincipalKind.Member, UserId, "Sam", source);

        source["region"] = "us";
        source["extra"] = "1";

        principal.Claim("region").ShouldBe("eu");
        principal.Claims.Count.ShouldBe(1);
    }
}
