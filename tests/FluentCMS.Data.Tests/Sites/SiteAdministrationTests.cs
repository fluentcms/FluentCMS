using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Events;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Sites;

// ISiteAdministration, the SDK's way to the sites for a plugin: sites.manage for the instance, the refusals of
// SiteManager, and an event after each write.
public sealed class SiteAdministrationTests : IDisposable
{
    private readonly TestDatabase _database = TestDatabase.Create(forSystemTables: true);
    private readonly List<CmsEvent> _events = [];
    private readonly ServiceProvider _services;

    public SiteAdministrationTests()
    {
        try
        {
            _services = _database.CreateServices(SampleSchema.FromClasses().Build(), cms =>
            {
                cms.Services.AddSingleton(_events);
                cms.Events.AddHandler<SiteCreatedEvent, Heard<SiteCreatedEvent>>();
                cms.Events.AddHandler<SiteDeletedEvent, Heard<SiteDeletedEvent>>();
            });
            using var scope = _services.CreateScope();
            scope.ServiceProvider.GetRequiredService<SystemDbContext>().Database.Migrate();
        }
        catch
        {
            // xunit disposes no test class whose constructor throws, and the database would outlive the test.
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _services?.Dispose();
        _database.Dispose();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreatingASiteMakesItsLocaleAndPublicRoleAndRaisesTheEventOfTheNewSite()
    {
        var site = await AsAsync(SuperAdmin(), sites => sites.CreateAsync(" Docs ", "docs", "fr-ca", Token));

        (site.Name, site.Slug, site.DefaultLocale, site.IsDefault).ShouldBe(("Docs", "docs", "fr-CA", false));
        using (var scope = _services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            (await context.Set<SiteLocale>().AsNoTracking().Where(locale => locale.SiteId == site.Id).Select(locale => locale.Code).ToListAsync(Token)).ShouldBe(["fr-CA"]);
            (await context.Set<Role>().AsNoTracking().Where(role => role.SiteId == site.Id).Select(role => role.Name).ToListAsync(Token)).ShouldBe([BuiltInRoles.PublicName]);
        }

        var created = _events.ShouldHaveSingleItem().ShouldBeOfType<SiteCreatedEvent>();
        (created.SiteId, created.Slug, created.Source.SiteId).ShouldBe((site.Id, "docs", site.Id));
        var found = (await AsAsync(SuperAdmin(), sites => sites.FindAsync(new SiteId(site.Id), Token))).ShouldNotBeNull();
        (found.Id, found.Name, found.Slug, found.DefaultLocale, found.IsDefault).ShouldBe((site.Id, "Docs", "docs", "fr-CA", false));
    }

    [Fact]
    public async Task DeletingASiteRaisesTheEventOfNoSiteAndRefusesWhatSiteManagerRefuses()
    {
        var site = await AsAsync(SuperAdmin(), sites => sites.CreateAsync("Docs", "docs", "en", Token));
        _events.Clear();

        (await RefusalAsync(sites => sites.DeleteAsync(new SiteId(DefaultSite.Id), Token))).ShouldBe("FCMS2302");
        (await RefusalAsync(sites => sites.DeleteAsync(new SiteId(Guid.CreateVersion7()), Token))).ShouldBe("FCMS2301");
        (await RefusalAsync(sites => sites.CreateAsync("Docs again", "docs", "en", Token))).ShouldBe("FCMS2304");
        (await RefusalAsync(sites => sites.CreateAsync(new string('n', 129), "long", "en", Token))).ShouldBe("FCMS2304");
        _events.ShouldBeEmpty();

        await AsAsync(SuperAdmin(), async sites =>
        {
            await sites.DeleteAsync(new SiteId(site.Id), Token);
            return true;
        });

        var deleted = _events.ShouldHaveSingleItem().ShouldBeOfType<SiteDeletedEvent>();
        (deleted.SiteId, deleted.Slug, deleted.Source.SiteId).ShouldBe((site.Id, "docs", Guid.Empty));
        (await AsAsync(SuperAdmin(), sites => sites.FindAsync(new SiteId(site.Id), Token))).ShouldBeNull();
    }

    // sites.manage for the instance: a role of a site and a grant limited to a site do not hold it, and nothing is allowed
    // outside an operation.
    [Fact]
    public async Task EveryCallNeedsSitesManageForTheInstance()
    {
        Guid ofSite, limited;
        using (var scope = _services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            var siteRole = new Role { Id = Guid.CreateVersion7(), Name = "Site owner", SiteId = DefaultSite.Id };
            var limitedRole = new Role { Id = Guid.CreateVersion7(), Name = "Limited owner" };
            context.AddRange(
                siteRole,
                limitedRole,
                new RoleGrant { Id = Guid.CreateVersion7(), RoleId = siteRole.Id, Permission = CorePermissions.SitesManage },
                new RoleGrant { Id = Guid.CreateVersion7(), RoleId = limitedRole.Id, Permission = CorePermissions.SitesManage, Constraints = new PermissionConstraints { Sites = [new SiteId(DefaultSite.Id)] }.ToJson() });
            await context.SaveChangesAsync(Token);
            (ofSite, limited) = (siteRole.Id, limitedRole.Id);
        }

        foreach (var principal in (Principal[])[User(ofSite), User(limited), User(BuiltInRoles.Editor), Principal.Anonymous])
        {
            await Should.ThrowAsync<ManagementAccessDeniedException>(() => AsAsync(principal, sites => sites.CreateAsync("Docs", "docs", "en", Token)));
            await Should.ThrowAsync<ManagementAccessDeniedException>(() => AsAsync(principal, sites => sites.FindAsync(new SiteId(DefaultSite.Id), Token)));
            await Should.ThrowAsync<ManagementAccessDeniedException>(() => AsAsync(principal, async sites =>
            {
                await sites.DeleteAsync(new SiteId(DefaultSite.Id), Token);
                return true;
            }));
        }

        using var outside = _services.CreateScope();
        (await Should.ThrowAsync<InvalidOperationException>(() => outside.ServiceProvider.GetRequiredService<ISiteAdministration>().FindAsync(new SiteId(DefaultSite.Id), Token)))
            .Message.ShouldStartWith("FCMS1408");
        _events.ShouldBeEmpty();
    }

    private static Principal SuperAdmin() => User(BuiltInRoles.SuperAdmin);

    private static Principal User(Guid role) =>
        new(PrincipalKind.AdminUser, Guid.CreateVersion7(), "User", new Dictionary<string, string>
        {
            [ClaimPrincipalRoles.Claim] = role.ToString(),
            [TokenScopes.Claim] = TokenScopes.Management,
        });

    private async Task<string> RefusalAsync<T>(Func<ISiteAdministration, Task<T>> call) =>
        (await Should.ThrowAsync<ValidationException>(() => AsAsync(SuperAdmin(), call))).Errors.ShouldHaveSingleItem().Code;

    private Task<string> RefusalAsync(Func<ISiteAdministration, Task> call) =>
        RefusalAsync(async sites =>
        {
            await call(sites);
            return true;
        });

    private async Task<T> AsAsync<T>(Principal principal, Func<ISiteAdministration, Task<T>> call)
    {
        using var scope = _services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>();
        accessor.Current = new OperationContext(principal, Origin.Api, new SiteId(DefaultSite.Id), "en", null, "correlation", DateTimeOffset.UtcNow);
        try
        {
            return await call(scope.ServiceProvider.GetRequiredService<ISiteAdministration>());
        }
        finally
        {
            accessor.Current = null;
        }
    }

    private sealed class Heard<TEvent>(List<CmsEvent> events) : IEventHandler<TEvent>
        where TEvent : CmsEvent
    {
        public ValueTask HandleAsync(TEvent message, CancellationToken cancellationToken)
        {
            events.Add(message);
            return ValueTask.CompletedTask;
        }
    }
}
