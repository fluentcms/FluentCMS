using FluentCMS.Authorization;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Schema;
using FluentCMS.Schema;
using FluentCMS.Sites;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Authorization;

public class ManagementAuthorityTests
{
    private static readonly SiteId Site = new(Guid.CreateVersion7());

    private static readonly PrincipalBinding Unbound = new(null, null);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WithoutAnOperationEveryCheckRefusesAndNamesTheOneWayAsTheSystem()
    {
        var accessor = new OperationContextAccessor();
        var authority = new ManagementAuthority(new Evaluator(), accessor);
        var checker = new PermissionChecker(authority);

        foreach (var check in Checks(authority, checker))
        {
            var refused = await Should.ThrowAsync<InvalidOperationException>(check);

            refused.Message.ShouldStartWith(AuthorizationErrorCodes.NoOperation + ":");
            refused.Message.ShouldContain(nameof(SystemOperation) + "." + nameof(SystemOperation.Begin));
        }
    }

    [Fact]
    public async Task ASystemOperationAllowsEveryCheckUntilItEnds()
    {
        var accessor = new OperationContextAccessor();
        var authority = new ManagementAuthority(new Evaluator(), accessor);
        var checker = new PermissionChecker(authority);

        foreach (var origin in new[] { Origin.Job, Origin.Cli, Origin.Seed })
        {
            using (SystemOperation.Begin(accessor, TimeProvider.System, origin, Site))
            {
                foreach (var check in Checks(authority, checker))
                {
                    await check();
                }

                (await authority.AllowsAsync(CorePermissions.UsersManage, site: null, Token)).ShouldBeTrue();
                (await checker.AllowsAsync("media.manage", Site, Token)).ShouldBeTrue();
                (await checker.CanManageAsync(Token)).ShouldBeTrue();
                (await checker.HoldsGrantInAsync(new SiteId(Guid.CreateVersion7()), Token)).ShouldBeTrue();
            }

            await Should.ThrowAsync<InvalidOperationException>(() => authority.AllowsAsync(CorePermissions.UsersManage, site: null, Token).AsTask());
        }
    }

    [Fact]
    public async Task AnOperationOfAPrincipalWithoutTheGrantIsDeniedNotRefused()
    {
        var accessor = new OperationContextAccessor { Current = new OperationContext(Principal.Anonymous, Origin.Api, Site, "en", null, "test", DateTimeOffset.UtcNow) };
        var authority = new ManagementAuthority(new Evaluator(), accessor);

        (await authority.AllowsAsync(CorePermissions.UsersManage, site: null, Token)).ShouldBeFalse();
        (await authority.CoversAsync([("content.page.read", PermissionConstraints.None)], null, Unbound, Token)).ShouldBeFalse();
        await Should.ThrowAsync<PermissionDeniedException>(() => new PermissionChecker(authority).RequireAsync("media.manage", Site, Token).AsTask());
        (await new PermissionChecker(authority).HoldsGrantInAsync(Site, Token)).ShouldBeFalse();
    }

    // A grant reaches a site through a role of that site or of the instance, within its own site limit.
    [Fact]
    public async Task AGrantReachesTheSitesOfItsRoleAndOfItsLimit()
    {
        var other = new SiteId(Guid.CreateVersion7());
        var principal = new Principal(PrincipalKind.ApiKey, Guid.CreateVersion7(), "key", [new(TokenScopes.Claim, "management")]);
        var accessor = new OperationContextAccessor { Current = new OperationContext(principal, Origin.Api, Site, "en", null, "test", DateTimeOffset.UtcNow) };

        async Task<(bool Site, bool Other)> ReachAsync(params EffectiveGrant[] grants)
        {
            var checker = new PermissionChecker(new ManagementAuthority(new Evaluator(grants), accessor));
            return (await checker.HoldsGrantInAsync(Site, Token), await checker.HoldsGrantInAsync(other, Token));
        }

        (await ReachAsync(new EffectiveGrant("media.read", PermissionConstraints.None, Site.Value))).ShouldBe((true, false));
        (await ReachAsync(new EffectiveGrant("content.*.read", new PermissionConstraints { Sites = [other] }, roleSite: null))).ShouldBe((false, true));
        (await ReachAsync(new EffectiveGrant("schema.read", PermissionConstraints.None, roleSite: null))).ShouldBe((true, true));
        (await ReachAsync()).ShouldBe((false, false));
    }

    // #3595: a grant that reaches the site counts only when it gives a permission there, as Allows and ResourcesOf decide.
    [Theory]
    [InlineData("content.page.read", null, null, true)]
    [InlineData("users.manage", null, null, true)]
    [InlineData("content.page.read", "own-entries", null, true)]
    [InlineData("*", "own-entries", null, true)]
    [InlineData("*.*.update", "own-entries", null, true)]
    [InlineData("media.read", null, "folder", true)]
    [InlineData("*", null, "folder", true)]
    [InlineData("users.manage", "own-entries", null, false)]
    [InlineData("settings.*.manage", "own-entries", null, false)]
    [InlineData("content.*.read", null, "folder", false)]
    [InlineData("media.read", "own-entries", "folder", false)]
    [InlineData("media.read", null, "", false)]
    public async Task OnlyAGrantThatGivesAPermissionInTheSiteCounts(string pattern, string? rowRule, string? resource, bool counts)
    {
        var principal = new Principal(PrincipalKind.ApiKey, Guid.CreateVersion7(), "key", [new(TokenScopes.Claim, "management")]);
        var accessor = new OperationContextAccessor { Current = new OperationContext(principal, Origin.Api, Site, "en", null, "test", DateTimeOffset.UtcNow) };
        var constraints = new PermissionConstraints { RowRule = rowRule, Resources = resource is null ? null : resource.Length == 0 ? [] : [resource] };
        var checker = new PermissionChecker(new ManagementAuthority(new Evaluator(new EffectiveGrant(pattern, constraints, Site.Value)), accessor));

        (await checker.HoldsGrantInAsync(Site, Token)).ShouldBe(counts);
    }

    // Only a token or key for managing may manage, and the mcp scope on the MCP server: the anonymous caller, a Delivery or
    // Preview key, a preview token and a token without the scope read content alone, whatever their roles grant.
    [Theory]
    [InlineData(null, Origin.Api, false)]
    [InlineData("delivery", Origin.Api, false)]
    [InlineData("preview", Origin.Api, false)]
    [InlineData("openid draft", Origin.Api, false)]
    [InlineData("mcp", Origin.Api, false)]
    [InlineData("openid management", Origin.Api, true)]
    [InlineData("management", Origin.Admin, true)]
    [InlineData("mcp", Origin.Mcp, true)]
    [InlineData("delivery", Origin.Mcp, false)]
    [InlineData("management", Origin.Mcp, true)]
    public async Task OnlyAPrincipalWhoseTokenIsForManagingMayManage(string? scope, Origin origin, bool manages)
    {
        var principal = scope is null ? Principal.Anonymous : new Principal(PrincipalKind.ApiKey, Guid.CreateVersion7(), "key", [new(TokenScopes.Claim, scope)]);
        var accessor = new OperationContextAccessor { Current = new OperationContext(principal, origin, Site, "en", null, "test", DateTimeOffset.UtcNow) };

        (await new PermissionChecker(new ManagementAuthority(new Evaluator(), accessor)).CanManageAsync(Token)).ShouldBe(manages);
    }

    private static Func<Task>[] Checks(ManagementAuthority authority, PermissionChecker checker) =>
    [
        () => authority.AllowsAsync(CorePermissions.UsersManage, site: null, Token).AsTask(),
        () => authority.RequireAsync(CorePermissions.SitesManage, Site.Value, Token).AsTask(),
        () => authority.CoversAsync([("content.page.read", PermissionConstraints.None)], null, Unbound, Token).AsTask(),
        () => checker.AllowsAsync("media.manage", Site, Token).AsTask(),
        () => checker.RequireAsync("media.manage", site: null, Token).AsTask(),
        () => checker.CanManageAsync(Token).AsTask(),
        () => checker.HoldsGrantInAsync(Site, Token).AsTask(),
    ];

    // The system principal holds every grant without any being stored, and any other holds the grants given, none by default.
    private sealed class Evaluator(params EffectiveGrant[] grants) : IAuthorizationEvaluator
    {
        private static readonly RowRuleRegistry Rules = new(new SchemaBuilder().Build(), new ContentQueryOptions(), []);

        public ValueTask<EffectivePermissions> ResolveAsync(OperationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new EffectivePermissions(context, grants, Rules));

        public ValueTask<bool> CanAccessEntryAsync(OperationContext context, SchemaType type, ContentOperation operation, Guid entry, AccessTarget target, QueryStatus status = QueryStatus.Published, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<WritableFields> WritableFieldsAsync(OperationContext context, SchemaType type, ContentOperation operation, Guid? entry, AccessTarget target, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
