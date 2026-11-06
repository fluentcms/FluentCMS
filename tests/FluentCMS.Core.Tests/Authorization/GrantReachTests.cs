using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Sites;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Authorization;

// What a caller may hand out: a grant is held only through grants that reach at least as far.
public class GrantReachTests
{
    private static readonly Guid SiteA = Guid.CreateVersion7();
    private static readonly Guid SiteB = Guid.CreateVersion7();
    private static readonly PrincipalBinding Unbound = new(null, null);

    [Theory]
    [InlineData("*", "users.manage", true)]
    [InlineData("*", "*", true)]
    [InlineData("content.*.read", "content.page.read", true)]
    [InlineData("content.*.read", "content.*.read", true)]
    [InlineData("content.page.read", "content.*.read", false)]
    [InlineData("content.*.*", "content.page.read", true)]
    [InlineData("content.page.read", "*", false)]
    [InlineData("users.manage", "content.page.read", false)]
    [InlineData("content.page.read", "content.page.update", false)]
    public void APatternCoversWhatItMatchesWhereverTheOtherDoes(string grant, string other, bool covers) =>
        PermissionPattern.Covers(grant, other).ShouldBe(covers);

    [Theory]
    [InlineData("*", "content.page.*", "content.page.*")]
    [InlineData("content.*.read", "content.page.*", "content.page.read")]
    [InlineData("content.post.read", "content.page.*", null)]
    [InlineData("users.manage", "content.page.*", null)]
    public void PatternsIntersectToWhatBothMatch(string grant, string other, string? both) =>
        PermissionPattern.Intersect(grant, other).ShouldBe(both);

    [Fact]
    public void ARoleOfASiteAndASiteBindingNarrowTheReach()
    {
        var reach = GrantReach.Of("*", PermissionConstraints.None, SiteA, new PrincipalBinding(new HashSet<Guid> { SiteA, SiteB }, new HashSet<string> { "page" })).ShouldHaveSingleItem();

        reach.Pattern.ShouldBe("content.page.*");
        reach.Sites.ShouldBe([SiteA]);
        GrantReach.Of("*", PermissionConstraints.None, SiteA, new PrincipalBinding(new HashSet<Guid> { SiteB }, null)).ShouldBeEmpty();
    }

    [Fact]
    public void AReachIncludesOnlyWhatHasNoFewerLimits()
    {
        var everywhere = Reach("content.*.read", new PermissionConstraints());
        var limited = Reach("content.page.read", new PermissionConstraints { Sites = [new SiteId(SiteA)], Locales = ["en"], Fields = ["title"], RowRule = "own-entries" });

        everywhere.Includes(limited).ShouldBeTrue();
        limited.Includes(everywhere).ShouldBeFalse();
        Reach("content.page.read", new PermissionConstraints { RowRule = "own-entries" }).Includes(Reach("content.page.read", new PermissionConstraints())).ShouldBeFalse();
        Reach("content.page.read", new PermissionConstraints { Locales = ["EN", "fr"] }).Includes(Reach("content.page.read", new PermissionConstraints { Locales = ["en"] })).ShouldBeTrue();
        Reach("content.page.read", new PermissionConstraints { Fields = ["title"] }).Includes(Reach("content.page.read", new PermissionConstraints { Fields = ["title", "body"] })).ShouldBeFalse();
    }

    [Fact]
    public void APrincipalCoversAGrantPerSiteThroughSeveralOfItsOwn()
    {
        var permissions = Permissions(
            new EffectiveGrant("content.page.read", PermissionConstraints.None, SiteA),
            new EffectiveGrant("content.page.read", PermissionConstraints.None, SiteB));

        permissions.Covers(Reach("content.page.read", new PermissionConstraints { Sites = [new SiteId(SiteA), new SiteId(SiteB)] })).ShouldBeTrue();
        permissions.Covers(Reach("content.page.read", new PermissionConstraints())).ShouldBeFalse();
        permissions.Covers(Reach("content.page.update", new PermissionConstraints { Sites = [new SiteId(SiteA)] })).ShouldBeFalse();
    }

    [Fact]
    public void APrincipalWithoutTheManagementScopeCoversNothing()
    {
        var principal = new Principal(PrincipalKind.ApiKey, Guid.CreateVersion7(), "key", new Dictionary<string, string> { [TokenScopes.Claim] = TokenScopes.Delivery });
        var permissions = new EffectivePermissions(Operation(principal), [new EffectiveGrant("*", PermissionConstraints.None, null)], Rules());

        permissions.Covers(Reach("content.page.read", new PermissionConstraints())).ShouldBeFalse();
    }

    private static GrantReach Reach(string pattern, PermissionConstraints constraints) =>
        GrantReach.Of(pattern, constraints, roleSite: null, Unbound).ShouldHaveSingleItem();

    private static EffectivePermissions Permissions(params EffectiveGrant[] grants)
    {
        var principal = new Principal(PrincipalKind.AdminUser, Guid.CreateVersion7(), "admin", new Dictionary<string, string> { [TokenScopes.Claim] = TokenScopes.Management });
        return new EffectivePermissions(Operation(principal), grants, Rules());
    }

    private static OperationContext Operation(Principal principal) =>
        new(principal, Origin.Admin, new SiteId(SiteA), "en", null, "test", DateTimeOffset.UtcNow);

    private static RowRuleRegistry Rules() => new(new FluentCMS.Core.Schema.SchemaBuilder().Build(), new FluentCMS.Core.Content.Query.ContentQueryOptions(), []);
}
