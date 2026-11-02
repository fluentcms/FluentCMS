using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Query;
using FluentCMS.Schema;
using FluentCMS.Sites;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluentCMS.Data.Tests.Authorization;

// The query database with the real evaluator and seeded roles. Every call is a request of its own, in a new scope.
internal sealed class AuthorizationDatabase(Action<FluentCmsBuilder>? configure = null) : IDisposable
{
    public static readonly SiteId SiteA = new(QueryDatabase.SiteA);
    public static readonly SiteId SiteB = new(QueryDatabase.SiteB);

    public static readonly AccessTarget InSiteA = new(SiteA, "en");

    public QueryDatabase Database { get; } = new(configure);

    public static SchemaType Article => QueryDatabase.Schema.FindContentType("article")!;

    public static Principal User(Guid[] roles, string[]? scopes = null, Guid? id = null, Dictionary<string, string>? claims = null)
    {
        var all = new Dictionary<string, string>(claims ?? [])
        {
            [ClaimPrincipalRoles.Claim] = string.Join(' ', roles),
        };

        if (scopes is not null)
        {
            all[TokenScopes.Claim] = string.Join(' ', scopes);
        }

        return new Principal(PrincipalKind.AdminUser, id ?? Guid.CreateVersion7(), "User", all);
    }

    public static OperationContext Operation(Principal principal, Origin origin = Origin.Api, SiteId? site = null) =>
        new(principal, origin, site ?? SiteA, "en", null, "correlation", DateTimeOffset.UtcNow);

    public async Task<ContentQueryResult> QueryAsync(Principal principal, string query, string type = "article", SiteId? site = null, Origin origin = Origin.Api)
    {
        using var scope = Database.NewScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>();
        accessor.Current = Operation(principal, origin, site);
        try
        {
            var parsed = BracketQueryParser.Parse(query) with { Site = site ?? SiteA };
            return await scope.ServiceProvider.GetRequiredService<ContentQueryEngine>().ExecuteAsync(type, parsed, TestContext.Current.CancellationToken);
        }
        finally
        {
            accessor.Current = null;
        }
    }

    public async Task<EffectivePermissions> PermissionsAsync(Principal principal, Origin origin = Origin.Api, SiteId? site = null)
    {
        using var scope = Database.NewScope();
        return await scope.ServiceProvider.GetRequiredService<IAuthorizationEvaluator>().ResolveAsync(Operation(principal, origin, site), TestContext.Current.CancellationToken);
    }

    public async Task<bool> CanAccessEntryAsync(Principal principal, ContentOperation operation, Guid entry, QueryStatus status = QueryStatus.Published)
    {
        using var scope = Database.NewScope();
        return await scope.ServiceProvider.GetRequiredService<IAuthorizationEvaluator>().CanAccessEntryAsync(
            Operation(principal), Article, operation, entry, InSiteA, status, TestContext.Current.CancellationToken);
    }

    public async Task<WritableFields> WritableAsync(Principal principal, ContentOperation operation, Guid? entry)
    {
        using var scope = Database.NewScope();
        return await scope.ServiceProvider.GetRequiredService<IAuthorizationEvaluator>().WritableFieldsAsync(
            Operation(principal), Article, operation, entry, InSiteA, TestContext.Current.CancellationToken);
    }

    public Guid AddRole(string name, Guid? site = null, params (string Permission, PermissionConstraints? Constraints)[] grants)
    {
        using var scope = Database.NewScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        var role = new Role { Name = name, SiteId = site };
        context.Add(role);
        context.SaveChanges();

        foreach (var (permission, constraints) in grants)
        {
            context.Add(new RoleGrant { RoleId = role.Id, Permission = permission, Constraints = constraints?.ToJson() });
        }

        context.SaveChanges();
        return role.Id;
    }

    public void Grant(Guid role, string permission, PermissionConstraints? constraints = null)
    {
        using var scope = Database.NewScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        context.Add(new RoleGrant { RoleId = role, Permission = permission, Constraints = constraints?.ToJson() });
        context.SaveChanges();
    }

    public void Dispose() => Database.Dispose();
}
