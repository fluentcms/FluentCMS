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
            using (SystemOperation.Begin(accessor, origin, Site))
            {
                foreach (var check in Checks(authority, checker))
                {
                    await check();
                }

                (await authority.AllowsAsync(CorePermissions.UsersManage, site: null, Token)).ShouldBeTrue();
                (await checker.AllowsAsync("media.manage", Site, Token)).ShouldBeTrue();
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
    }

    private static Func<Task>[] Checks(ManagementAuthority authority, PermissionChecker checker) =>
    [
        () => authority.AllowsAsync(CorePermissions.UsersManage, site: null, Token).AsTask(),
        () => authority.RequireAsync(CorePermissions.SitesManage, Site.Value, Token).AsTask(),
        () => authority.CoversAsync([("content.page.read", PermissionConstraints.None)], null, Unbound, Token).AsTask(),
        () => checker.AllowsAsync("media.manage", Site, Token).AsTask(),
        () => checker.RequireAsync("media.manage", site: null, Token).AsTask(),
    ];

    // The system principal holds every grant without any being stored, and any other holds none here.
    private sealed class Evaluator : IAuthorizationEvaluator
    {
        private static readonly RowRuleRegistry Rules = new(new SchemaBuilder().Build(), new ContentQueryOptions(), []);

        public ValueTask<EffectivePermissions> ResolveAsync(OperationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new EffectivePermissions(context, [], Rules));

        public ValueTask<bool> CanAccessEntryAsync(OperationContext context, SchemaType type, ContentOperation operation, Guid entry, AccessTarget target, QueryStatus status = QueryStatus.Published, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<WritableFields> WritableFieldsAsync(OperationContext context, SchemaType type, ContentOperation operation, Guid? entry, AccessTarget target, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
