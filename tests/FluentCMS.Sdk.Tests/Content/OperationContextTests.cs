using System.Runtime.CompilerServices;
using FluentCMS.Content;
using FluentCMS.Sites;
using Shouldly;
using Xunit;

namespace FluentCMS.Sdk.Tests.Content;

public class OperationContextTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 9, 12, 0, TimeSpan.Zero);

    private static OperationContext Create() => new(
        new Principal(PrincipalKind.AdminUser, Guid.Parse("00000000-0000-0000-0000-000000000001"), "Amir"),
        Origin.Admin,
        new SiteId(Guid.Parse("00000000-0000-0000-0000-0000000000a1")),
        "fr-CA",
        "admin-client",
        "corr-1",
        Now);

    [Fact]
    public void CarriesEveryField()
    {
        var context = Create();

        context.Principal.Name.ShouldBe("Amir");
        context.Origin.ShouldBe(Origin.Admin);
        context.Site.ShouldBe(new SiteId(Guid.Parse("00000000-0000-0000-0000-0000000000a1")));
        context.Locale.ShouldBe("fr-CA");
        context.ClientId.ShouldBe("admin-client");
        context.CorrelationId.ShouldBe("corr-1");
        context.Timestamp.ShouldBe(Now);
    }

    [Fact]
    public void ClientIdIsOptional()
    {
        var context = Create() with { ClientId = null };

        context.ClientId.ShouldBeNull();
    }

    [Fact]
    public void WithExpressionCreatesNewInstanceAndLeavesOriginalUnchanged()
    {
        var original = Create();

        var changed = original with { Origin = Origin.Mcp, Locale = "en" };

        changed.ShouldNotBeSameAs(original);
        changed.Origin.ShouldBe(Origin.Mcp);
        changed.Locale.ShouldBe("en");
        original.Origin.ShouldBe(Origin.Admin);
        original.Locale.ShouldBe("fr-CA");
    }

    [Fact]
    public void HasNoPublicSetters()
    {
        var mutable = typeof(OperationContext).GetProperties()
            .Where(p => p.SetMethod is { IsPublic: true }
                && !p.SetMethod.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit)));

        mutable.ShouldBeEmpty();
    }

    [Fact]
    public void EqualWhenAllFieldsAreEqual()
    {
        var a = Create();
        var b = Create();

        a.ShouldNotBeSameAs(b);
        a.Equals(b).ShouldBeTrue();
        (a == b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void NotEqualWhenAnyFieldDiffers()
    {
        var original = Create();

        var variants = new[]
        {
            original with { Principal = Principal.System },
            original with { Origin = Origin.Api },
            original with { Site = new SiteId(Guid.Parse("00000000-0000-0000-0000-0000000000a2")) },
            original with { Locale = "en" },
            original with { ClientId = "other" },
            original with { CorrelationId = "corr-2" },
            original with { Timestamp = Now.AddSeconds(1) },
        };

        foreach (var variant in variants)
        {
            variant.Equals(original).ShouldBeFalse();
        }
    }

    [Fact]
    public void PrincipalsAreComparedByKindAndId()
    {
        var id = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var a = Create();
        var b = a with { Principal = new Principal(PrincipalKind.AdminUser, id, "Renamed") };

        a.Equals(b).ShouldBeTrue();
    }

    [Fact]
    public void AccessorHoldsAndClearsTheCurrentContext()
    {
        IOperationContextAccessor accessor = new FakeAccessor();
        accessor.Current.ShouldBeNull();

        var context = Create();
        accessor.Current = context;
        accessor.Current.ShouldBeSameAs(context);

        accessor.Current = null;
        accessor.Current.ShouldBeNull();
    }

    private sealed class FakeAccessor : IOperationContextAccessor
    {
        public OperationContext? Current { get; set; }
    }
}
