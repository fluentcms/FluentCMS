using System.Net;
using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Caching;
using FluentCMS.Core.Events;
using FluentCMS.Core.Identity;
using FluentCMS.Core.IntegrationTests.Testing;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Providers;
using FluentCMS.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Management;

// The last active Super Admin is kept (#2850): deactivating its user, taking the role from it and editing a role so that it
// no longer grants everything are refused when they would leave no active user who holds `*`, whoever asks, and two such
// changes at once on two instances leave one.
public sealed class SuperAdminTests : IAsyncDisposable
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("fluentcms-super-admin-");

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            _directory.Delete(recursive: true);
        }
        catch (IOException)
        {
        }

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task TheLastActiveSuperAdminCannotTakeTheRoleFromItself()
    {
        await using var host = NewHost();
        var owner = await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token);
        using var client = await host.AsUserAsync(owner);

        var refused = await AdminFixture.SendAsync(client, HttpMethod.Delete, $"users/{owner.Id:D}/roles/{BuiltInRoles.SuperAdmin:D}");
        refused.Status.ShouldBe(HttpStatusCode.Conflict, refused.Body?.ToJsonString());
        refused.Json["code"]!.GetValue<string>().ShouldBe(IdentityErrorCodes.LastSuperAdmin);
        refused.Json["errors"]!.AsArray().ShouldHaveSingleItem()!["path"]!.GetValue<string>().ShouldBe("id");
        (await HoldersAsync(host)).ShouldBe([owner.Id]);

        var row = (await RefusalsAsync(host)).ShouldHaveSingleItem();
        row.Action.ShouldBe("user.change_refused");
        row.TargetType.ShouldBe("user");
        row.TargetId.ShouldBe(owner.Id.ToString("D"));
        row.ActorId.ShouldBe(owner.Id);
        row.ActorType.ShouldBe(PrincipalKind.AdminUser);
        row.SiteId.ShouldBeNull();
        Details(row)["change"].ShouldBe("remove_role");
        Details(row)["code"].ShouldBe(IdentityErrorCodes.LastSuperAdmin);

        // The refused change raised nothing: its event rolled back with it, and the one row is the role it was given.
        await host.Services.GetRequiredService<OutboxDrainer>().DrainAsync(Token);
        (await ActivityAsync(host, "user.role_changed")).Where(item => item.TargetId == owner.Id.ToString("D")).ShouldHaveSingleItem();

        var other = await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token);
        (await AdminFixture.SendAsync(client, HttpMethod.Delete, $"users/{owner.Id:D}/roles/{BuiltInRoles.SuperAdmin:D}")).Status.ShouldBe(HttpStatusCode.OK);
        (await HoldersAsync(host)).ShouldBe([other.Id]);
    }

    [Fact]
    public async Task AManagementKeyBoundToSuperAdminCannotDeactivateOrDemoteTheLastActiveAdminUser()
    {
        await using var host = NewHost();
        var admin = await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token);
        var key = await host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token);
        using var client = host.AsKey(key);

        foreach (var (method, path) in new[] { (HttpMethod.Post, $"users/{admin.Id:D}/deactivate"), (HttpMethod.Delete, $"users/{admin.Id:D}/roles/{BuiltInRoles.SuperAdmin:D}") })
        {
            var refused = await AdminFixture.SendAsync(client, method, path);
            refused.Status.ShouldBe(HttpStatusCode.Conflict, refused.Body?.ToJsonString());
            refused.Json["code"]!.GetValue<string>().ShouldBe(IdentityErrorCodes.LastSuperAdmin);
        }

        var stored = await AdminFixture.SendAsync(client, HttpMethod.Get, $"users/{admin.Id:D}");
        stored.Json["deactivatedAt"].ShouldBeNull();
        (await HoldersAsync(host)).ShouldBe([admin.Id]);

        var rows = await RefusalsAsync(host);
        rows.Select(row => Details(row)["change"]).ShouldBe(["deactivate", "remove_role"], ignoreOrder: true);
        rows.ShouldAllBe(row => row.ActorType == PrincipalKind.ApiKey && row.ActorId == key.Id && row.TargetId == admin.Id.ToString("D"));

        // A second admin lets the first go, and is then the last one. A deactivated holder does not count.
        var other = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        (await AdminFixture.SendAsync(client, HttpMethod.Put, $"users/{other.Id:D}/roles/{BuiltInRoles.SuperAdmin:D}")).Status.ShouldBe(HttpStatusCode.OK);
        (await AdminFixture.SendAsync(client, HttpMethod.Post, $"users/{admin.Id:D}/deactivate")).Status.ShouldBe(HttpStatusCode.OK);
        var last = await AdminFixture.SendAsync(client, HttpMethod.Post, $"users/{other.Id:D}/deactivate");
        last.Status.ShouldBe(HttpStatusCode.Conflict, last.Body?.ToJsonString());
        last.Json["code"]!.GetValue<string>().ShouldBe(IdentityErrorCodes.LastSuperAdmin);
        (await HoldersAsync(host)).ShouldBe([other.Id]);
    }

    [Fact]
    public async Task ARoleEditThatTakesEveryPermissionFromTheLastHolderIsRefused()
    {
        await using var host = NewHost();
        var owners = await host.CreateRoleAsync("Owners", [CorePermissions.All], Token);
        var owner = await host.CreateUserAsync("Owners", cancellationToken: Token);
        using var client = await host.AsUserAsync(owner);
        const string narrower = """{ "name": "Owners", "grants": [ { "permission": "*", "constraints": { "locales": ["en"] } } ] }""";

        var refused = await AdminFixture.SendAsync(client, HttpMethod.Put, $"roles/{owners:D}", narrower);
        refused.Status.ShouldBe(HttpStatusCode.Conflict, refused.Body?.ToJsonString());
        refused.Json["code"]!.GetValue<string>().ShouldBe(IdentityErrorCodes.LastSuperAdmin);

        var role = await AdminFixture.SendAsync(client, HttpMethod.Get, $"roles/{owners:D}");
        var grant = role.Json["grants"]!.AsArray().ShouldHaveSingleItem()!;
        grant["permission"]!.GetValue<string>().ShouldBe(CorePermissions.All);
        grant["constraints"].ShouldBeNull();

        var row = (await RefusalsAsync(host)).ShouldHaveSingleItem();
        row.Action.ShouldBe("role.change_refused");
        row.TargetType.ShouldBe("role");
        row.TargetId.ShouldBe(owners.ToString("D"));
        row.TargetLabel.ShouldBe("Owners");
        Details(row)["change"].ShouldBe("update");
        (await ActivityAsync(host, "role.updated")).ShouldBeEmpty();

        // The name and the flag of the role still change while it grants everything.
        (await AdminFixture.SendAsync(client, HttpMethod.Put, $"roles/{owners:D}", """{ "name": "Owners", "requireTwoFactor": true, "grants": [ { "permission": "*" } ] }"""))
            .Status.ShouldBe(HttpStatusCode.OK);

        await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token);
        (await AdminFixture.SendAsync(client, HttpMethod.Put, $"roles/{owners:D}", narrower)).Status.ShouldBe(HttpStatusCode.OK);
    }

    // Two admins take the grant from each other at once, each through its own instance of the host on one database. Both
    // pass their permission checks and wait for the lock in the database, which the test holds until both are there; then
    // one change commits and the other finds no other holder.
    [Theory]
    [InlineData("deactivate")]
    [InlineData("remove_role")]
    public async Task TwoRemovalsAtOnceOnTwoInstancesLeaveOneSuperAdmin(string change)
    {
        var file = Path.Combine(_directory.FullName, $"{change}.db");
        var arrivals = new Arrivals();
        var firstLock = CommandSignals.OfLock(SuperAdminGuard.LockName);
        var secondLock = CommandSignals.OfLock(SuperAdminGuard.LockName);
        await using var first = NewHost(file, arrivals, firstLock);
        var ada = await first.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token);
        var bob = await first.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token);
        await using var second = NewHost(file, arrivals, secondLock);
        using var asAda = await first.AsUserAsync(ada);
        using var asBob = await second.AsUserAsync(bob);

        Task<ApiResponse> Remove(HttpClient client, Guid target) => change == "deactivate"
            ? AdminFixture.SendAsync(client, HttpMethod.Post, $"users/{target:D}/deactivate")
            : AdminFixture.SendAsync(client, HttpMethod.Delete, $"users/{target:D}/roles/{BuiltInRoles.SuperAdmin:D}");

        Task<ApiResponse>[] removals;
        var locks = ActivatorUtilities.CreateInstance<DatabaseDistributedLock>(first.Services);
        await using (var held = await locks.TryAcquireAsync(SuperAdminGuard.LockName, TimeSpan.Zero, Token))
        {
            held.ShouldNotBeNull();
            firstLock.Reads.Forget();
            secondLock.Reads.Forget();
            removals = [Task.Run(() => Remove(asAda, bob.Id), Token), Task.Run(() => Remove(asBob, ada.Id), Token)];
            await arrivals.Both.WaitAsync(Patience, Token);

            // Two reads of the row on each instance: each found the lock held, waited, and looked again.
            await firstLock.Reads.NextAsync(2, Patience);
            await secondLock.Reads.NextAsync(2, Patience);
            removals.ShouldAllBe(removal => !removal.IsCompleted);
        }

        var done = await Task.WhenAll(removals);
        done.Select(response => response.Status).Order().ShouldBe([HttpStatusCode.OK, HttpStatusCode.Conflict]);
        done.Single(response => response.Status == HttpStatusCode.Conflict).Json["code"]!.GetValue<string>().ShouldBe(IdentityErrorCodes.LastSuperAdmin);
        (await HoldersAsync(first)).ShouldHaveSingleItem().ShouldBeOneOf(ada.Id, bob.Id);
    }

    private static FluentCmsTestHost NewHost(string? file = null, Arrivals? arrivals = null, CommandSignals? commands = null)
    {
        var host = new FluentCmsTestHost().WithServices(services =>
        {
            services.Configure<RateLimitOptions>(limits => limits.Auth.PermitLimit = 10_000);
            if (arrivals is not null)
            {
                services.AddSingleton<IDistributedLock>(provider => new ArrivalsLock(ActivatorUtilities.CreateInstance<DatabaseDistributedLock>(provider), arrivals));
            }
        });
        if (commands is not null)
        {
            host.Configure(cms => cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(commands)));
        }

        return file is null ? host : host.UseSqlite(file);
    }

    // The active users who hold Super Admin, or another instance-wide role that grants `*`.
    private static async Task<List<Guid>> HoldersAsync(FluentCmsTestHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        return await (
            from membership in context.Set<CmsUserRole>()
            join user in context.Set<CmsUser>() on membership.UserId equals user.Id
            join role in context.Set<Role>() on membership.RoleId equals role.Id
            where user.DeactivatedAt == null && role.SiteId == null
                && context.Set<RoleGrant>().Any(grant => grant.RoleId == role.Id && grant.Permission == CorePermissions.All && grant.Constraints == null)
            select user.Id).Distinct().ToListAsync(Token);
    }

    private static Task<List<ActivityRow>> RefusalsAsync(FluentCmsTestHost host) => ActivityAsync(host, "change_refused");

    private static async Task<List<ActivityRow>> ActivityAsync(FluentCmsTestHost host, string action)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<ActivityRow>().AsNoTracking()
            .Where(row => row.Action.EndsWith(action))
            .OrderBy(row => row.OccurredAt)
            .ToListAsync(Token);
    }

    private static Dictionary<string, string?> Details(ActivityRow row) =>
        JsonSerializer.Deserialize<Dictionary<string, string?>>(row.Details ?? "{}")!;

    private sealed class Arrivals
    {
        private readonly TaskCompletionSource _both = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _count;

        public Task Both => _both.Task;

        public void Arrive()
        {
            if (Interlocked.Increment(ref _count) == 2)
            {
                _both.TrySetResult();
            }
        }
    }

    // Says when a change reaches the lock of the last Super Admin, so that the test lets the two race only once both wait on it.
    private sealed class ArrivalsLock(IDistributedLock inner, Arrivals arrivals) : IDistributedLock
    {
        public Task<IDistributedLockHandle?> TryAcquireAsync(string name, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (name == SuperAdminGuard.LockName)
            {
                arrivals.Arrive();
            }

            return inner.TryAcquireAsync(name, timeout, cancellationToken);
        }
    }
}
