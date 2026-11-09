using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Publishing;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Sites;

public sealed class SiteTests : IDisposable
{
    private readonly TestDatabase _database = TestDatabase.Create(forSystemTables: true);
    private readonly ServiceProvider _services;

    public SiteTests()
    {
        try
        {
            _services = _database.CreateServices(SampleSchema.FromClasses().Build());
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
    public void TheMigrationSeedsTheDefaultSiteWithLocaleEnAndItsOwnPublicRole()
    {
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();

        var site = context.Set<Site>().AsNoTracking().Single();
        site.Id.ShouldBe(DefaultSite.Id);
        (site.Slug, site.DefaultLocale, site.IsDefault).ShouldBe(("default", "en", true));

        var locale = context.Set<SiteLocale>().AsNoTracking().Single();
        (locale.SiteId, locale.Code, locale.Enabled, locale.Fallbacks).ShouldBe((DefaultSite.Id, "en", true, null));

        var role = context.Set<Role>().AsNoTracking().Single(item => item.SiteId == DefaultSite.Id);
        (role.Id, role.Name, role.BuiltIn).ShouldBe((DefaultSite.PublicRole, "Public", true));
        context.Set<RoleGrant>().Count(grant => grant.RoleId == role.Id).ShouldBe(0);
    }

    [Fact]
    public async Task TheEvaluatorPicksTheDefaultSitesPublicRoleNotTheInstanceWideOne()
    {
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        context.Add(new RoleGrant { RoleId = DefaultSite.PublicRole, Permission = "content.article.read" });
        context.Add(new RoleGrant { RoleId = BuiltInRoles.Public, Permission = "content.tag.read" });
        await context.SaveChangesAsync(Token);

        var evaluator = scope.ServiceProvider.GetRequiredService<IAuthorizationEvaluator>();
        var operation = new OperationContext(Principal.Anonymous, Origin.Api, new SiteId(DefaultSite.Id), "en", null, "correlation", DateTimeOffset.UtcNow);
        var permissions = await evaluator.ResolveAsync(operation, Token);

        var target = new AccessTarget(new SiteId(DefaultSite.Id), "en");
        permissions.Allows("content.article.read", target).ShouldBeTrue();
        permissions.Allows("content.tag.read", target).ShouldBeFalse();
    }

    [Fact]
    public async Task CreatingASiteCreatesItsLocaleAndItsPublicRoleTogether()
    {
        using var scope = _services.CreateScope();
        var site = await scope.ServiceProvider.GetRequiredService<SiteManager>().CreateAsync(" Docs ", "docs", "FR-ca", Token);

        using var read = _services.CreateScope();
        var context = read.ServiceProvider.GetRequiredService<CmsDbContext>();
        var stored = context.Set<Site>().AsNoTracking().Single(item => item.Id == site.Id);
        (stored.Name, stored.Slug, stored.DefaultLocale, stored.IsDefault).ShouldBe(("Docs", "docs", "fr-CA", false));
        context.Set<SiteLocale>().AsNoTracking().Single(item => item.SiteId == site.Id).Code.ShouldBe("fr-CA");

        var role = context.Set<Role>().AsNoTracking().Single(item => item.SiteId == site.Id);
        (role.Name, role.BuiltIn, role.PermissionsVersion).ShouldBe(("Public", true, 1));
    }

    [Theory]
    [InlineData("Docs", "Not A Slug", "en", "slug")]
    [InlineData("Docs", "default", "en", "slug")]
    [InlineData("Docs", "abc\n", "en", "slug")]
    [InlineData("", "docs", "en", "name")]
    [InlineData("Docs", "docs", "en_US", "defaultLocale")]
    public async Task ASiteWithABadNameSlugOrLocaleIsRejectedAndLeavesNothingBehind(string name, string slug, string locale, string path)
    {
        using var scope = _services.CreateScope();

        var error = (await Should.ThrowAsync<ValidationException>(() => scope.ServiceProvider.GetRequiredService<SiteManager>().CreateAsync(name, slug, locale, Token))).Errors.Single();

        error.Path.ShouldBe(path);
        scope.ServiceProvider.GetRequiredService<CmsDbContext>().Set<Site>().Count().ShouldBe(1);
    }

    [Fact]
    public async Task DeletingASiteDeletesItsRolesGrantsAndLocalesAndNothingElse()
    {
        Guid siteId;
        using (var scope = _services.CreateScope())
        {
            siteId = (await scope.ServiceProvider.GetRequiredService<SiteManager>().CreateAsync("Docs", "docs", "en", Token)).Id;
            var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            var reviewer = new Role { Name = "Reviewer", SiteId = siteId };
            context.Add(reviewer);
            await context.SaveChangesAsync(Token);
            context.Add(new RoleGrant { RoleId = reviewer.Id, Permission = "schema.read" });
            await context.SaveChangesAsync(Token);
        }

        using (var scope = _services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<SiteManager>().DeleteAsync(siteId, Token);
        }

        using (var scope = _services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            context.Set<Site>().Select(site => site.Id).ToList().ShouldBe([DefaultSite.Id]);
            context.Set<SiteLocale>().Select(locale => locale.SiteId).ToList().ShouldBe([DefaultSite.Id]);
            context.Set<Role>().Count(role => role.SiteId == siteId).ShouldBe(0);
            context.Set<Role>().Count().ShouldBe(6);
            context.Set<RoleGrant>().Select(grant => grant.RoleId).Distinct().Count().ShouldBe(3);
        }
    }

    [Fact]
    public async Task TheDatabaseRefusesASiteRoleWhoseSiteDoesNotExist()
    {
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        context.Add(new Role { Name = "Orphan", SiteId = Guid.CreateVersion7() });

        await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync(Token));
    }

    [Fact]
    public async Task TheDefaultSiteCannotBeDeleted()
    {
        using var scope = _services.CreateScope();

        var error = (await Should.ThrowAsync<ValidationException>(() => scope.ServiceProvider.GetRequiredService<SiteManager>().DeleteAsync(DefaultSite.Id, Token))).Errors.Single();

        error.Code.ShouldBe("FCMS2302");
    }

    [Fact]
    public async Task ASiteWithContentCannotBeDeletedNotEvenWhenItIsAllInTheTrash()
    {
        Guid siteId;
        using (var scope = _services.CreateScope())
        {
            siteId = (await scope.ServiceProvider.GetRequiredService<SiteManager>().CreateAsync("Docs", "docs", "en", Token)).Id;
            var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            var now = DateTimeOffset.UtcNow;
            context.Documents.Add(new Document
            {
                DocumentId = Guid.CreateVersion7(),
                Type = "post",
                SiteId = siteId,
                Status = EntryStatus.Draft,
                CreatedAt = now,
                UpdatedAt = now,
                DeletedAt = now,
            });
            await context.SaveChangesAsync(Token);
        }

        using (var scope = _services.CreateScope())
        {
            var error = (await Should.ThrowAsync<ValidationException>(() => scope.ServiceProvider.GetRequiredService<SiteManager>().DeleteAsync(siteId, Token))).Errors.Single();
            error.Code.ShouldBe("FCMS2303");
            scope.ServiceProvider.GetRequiredService<CmsDbContext>().Set<Site>().Count(site => site.Id == siteId).ShouldBe(1);
        }
    }

    [Fact]
    public async Task ASiteThatDoesNotExistCannotBeDeleted()
    {
        using var scope = _services.CreateScope();

        var error = (await Should.ThrowAsync<ValidationException>(() => scope.ServiceProvider.GetRequiredService<SiteManager>().DeleteAsync(Guid.CreateVersion7(), Token))).Errors.Single();

        error.Code.ShouldBe("FCMS2301");
    }
}
