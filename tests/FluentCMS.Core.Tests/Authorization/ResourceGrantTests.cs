using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Schema;
using FluentCMS.Sites;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Authorization;

// A grant limited to a plugin's resources, such as media folders (ADR 0004): only the resources question counts it.
public class ResourceGrantTests
{
    private static readonly SiteId Site = new(Guid.CreateVersion7());
    private static readonly PrincipalBinding Unbound = new(null, null);

    [Fact]
    public void AGrantLimitedToResourcesHoldsOnlyWhereResourcesAreAsked()
    {
        var permissions = Permissions(new EffectiveGrant("media.*", new PermissionConstraints { Resources = ["a", "b"] }, null));
        var target = new AccessTarget(Site, null);

        permissions.Allows("media.read", target).ShouldBeFalse();
        permissions.ResourcesOf("media.read", target).ShouldNotBeNull().ShouldBe(["a", "b"], ignoreOrder: true);
        permissions.ResourcesOf("webhooks.manage", target).ShouldNotBeNull().ShouldBeEmpty();
    }

    [Fact]
    public void AGrantWithNoResourceLimitReachesEveryResourceAndTheUnionOfLimitedOnesIsTaken()
    {
        var target = new AccessTarget(Site, null);

        Permissions(
            new EffectiveGrant("media.read", new PermissionConstraints { Resources = ["a"] }, null),
            new EffectiveGrant("media.read", PermissionConstraints.None, null)).ResourcesOf("media.read", target).ShouldBeNull();
        Permissions(
            new EffectiveGrant("media.read", new PermissionConstraints { Resources = ["a"] }, null),
            new EffectiveGrant("media.*", new PermissionConstraints { Resources = ["b"] }, null)).ResourcesOf("media.read", target).ShouldBe(["a", "b"], ignoreOrder: true);
        Permissions(new EffectiveGrant("media.read", new PermissionConstraints { Resources = ["a"], Sites = [new SiteId(Guid.CreateVersion7())] }, null))
            .ResourcesOf("media.read", target).ShouldNotBeNull().ShouldBeEmpty();
    }

    [Fact]
    public void AStarGrantLimitedToResourcesGivesNoContentPermission()
    {
        var page = new SchemaBuilder().AddCollection("page", type => type.Field("title", "text")).Build().ContentTypes.Single();
        var permissions = Permissions(new EffectiveGrant("*", new PermissionConstraints { Resources = ["a"] }, null));

        permissions.Allows(page, ContentOperation.Read, new AccessTarget(Site, "en")).ShouldBeFalse();
        permissions.Allows("users.manage", new AccessTarget(Site, null)).ShouldBeFalse();
    }

    [Fact]
    public void AReachLimitedToResourcesIncludesOnlyFewerResources()
    {
        var all = Reach(PermissionConstraints.None);
        var ab = Reach(new PermissionConstraints { Resources = ["a", "b"] });
        var a = Reach(new PermissionConstraints { Resources = ["a"] });

        all.Includes(ab).ShouldBeTrue();
        ab.Includes(a).ShouldBeTrue();
        a.Includes(ab).ShouldBeFalse();
        ab.Includes(all).ShouldBeFalse();
    }

    private static GrantReach Reach(PermissionConstraints constraints) =>
        GrantReach.Of("media.read", constraints, roleSite: null, Unbound).ShouldHaveSingleItem();

    private static EffectivePermissions Permissions(params EffectiveGrant[] grants)
    {
        var principal = new Principal(PrincipalKind.AdminUser, Guid.CreateVersion7(), "admin", new Dictionary<string, string> { [TokenScopes.Claim] = TokenScopes.Management });
        var operation = new OperationContext(principal, Origin.Admin, Site, "en", null, "test", DateTimeOffset.UtcNow);
        return new EffectivePermissions(operation, grants, new RowRuleRegistry(new SchemaBuilder().Build(), new ContentQueryOptions(), []));
    }
}
