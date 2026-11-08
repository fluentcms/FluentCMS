using FluentCMS.Core.Authorization;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Authorization;

public sealed class RoleTests : IDisposable
{
    private static readonly Guid SiteA = Guid.Parse("0192e0a0-0000-7000-8000-0000000000a1");
    private static readonly Guid SiteB = Guid.Parse("0192e0a0-0000-7000-8000-0000000000b2");

    private readonly TestDatabase _database = TestDatabase.Create();
    private readonly ServiceProvider _services;

    public RoleTests()
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

    private static Role NewRole(string name, Guid? siteId = null) => new() { Name = name, SiteId = siteId };

    private static Site NewSite(Guid id) => new() { Id = id, Name = "Site", Slug = id.ToString("N"), DefaultLocale = "en", CreatedAt = DateTimeOffset.UtcNow };

    private static RoleGrant NewGrant(Guid roleId, string permission, PermissionConstraints? constraints = null) =>
        new() { RoleId = roleId, Permission = permission, Constraints = constraints?.ToJson() };

    private static long VersionOf(CmsDbContext context, Guid roleId) =>
        context.Set<Role>().AsNoTracking().Single(role => role.Id == roleId).PermissionsVersion;

    private static string[] GrantsOf(CmsDbContext context, Guid roleId) =>
        [.. context.Set<RoleGrant>().AsNoTracking().Where(grant => grant.RoleId == roleId).Select(grant => grant.Permission).OrderBy(permission => permission)];

    [Fact]
    public void TheMigrationSeedsTheBuiltInRolesWithFixedIds()
    {
        using var scope = _services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<Role>().AsNoTracking().ToList();

        roles.Select(role => (role.Id, role.Name)).ShouldBe(
            [
                (DefaultSite.PublicRole, "Public"),
                (BuiltInRoles.SuperAdmin, "Super Admin"),
                (BuiltInRoles.Editor, "Editor"),
                (BuiltInRoles.Author, "Author"),
                (BuiltInRoles.Public, "Public"),
                (BuiltInRoles.Member, "Member"),
            ],
            ignoreOrder: true);
        roles.ShouldAllBe(role => role.BuiltIn && role.SiteId == (role.Id == DefaultSite.PublicRole ? DefaultSite.Id : null));

        // Author's seeded grants changed in a later migration, which raises its stamp.
        roles.ShouldAllBe(role => role.PermissionsVersion == (role.Id == BuiltInRoles.Author ? 2 : 1));
    }

    [Fact]
    public void PublicAndMemberStartWithNoGrantsSoPublicAccessIsClosed()
    {
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();

        GrantsOf(context, BuiltInRoles.Public).ShouldBeEmpty();
        GrantsOf(context, BuiltInRoles.Member).ShouldBeEmpty();
    }

    [Fact]
    public void SuperAdminHoldsEverythingAndEditorCannotPurge()
    {
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();

        GrantsOf(context, BuiltInRoles.SuperAdmin).ShouldBe(["*"]);
        GrantsOf(context, BuiltInRoles.Editor).ShouldBe(
            [
                "content.*.create", "content.*.delete", "content.*.publish", "content.*.read", "content.*.restore",
                "content.*.unpublish", "content.*.update", "schema.read",
            ]);
    }

    [Fact]
    public void AuthorReadsEditsAndDeletesOnlyTheirOwnEntries()
    {
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();

        var grants = context.Set<RoleGrant>().AsNoTracking().Where(grant => grant.RoleId == BuiltInRoles.Author).ToList();

        grants.Select(grant => grant.Permission).Order().ShouldBe(["content.*.create", "content.*.delete", "content.*.read", "content.*.update", "schema.read"]);
        foreach (var grant in grants)
        {
            var rule = PermissionConstraints.FromJson(grant.Constraints).RowRule;
            rule.ShouldBe(grant.Permission is "content.*.read" or "content.*.update" or "content.*.delete" ? "own-entries" : null);
        }
    }

    [Fact]
    public void TheMigrationRaisesTheStampOfALiveAuthorRoleRelativeToWhereItIs()
    {
        using var database = TestDatabase.Create();
        using var services = database.CreateServices();
        using (var scope = services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
            var migrator = context.GetInfrastructure().GetRequiredService<IMigrator>();
            var migrations = context.Database.GetMigrations().ToList();
            var authorAt = migrations.FindIndex(name => name.EndsWith("_AuthorReadsOwnEntries", StringComparison.Ordinal));
            migrator.Migrate(migrations[authorAt - 1]);

            context.Set<Role>().Where(role => role.Id == BuiltInRoles.Author).ExecuteUpdate(role => role.SetProperty(item => item.PermissionsVersion, 7));
            context.Set<RoleGrant>().AsNoTracking().Single(grant => grant.RoleId == BuiltInRoles.Author && grant.Permission == "content.*.read").Constraints.ShouldBeNull();

            migrator.Migrate(migrations[authorAt]);

            // The columns this migration knows; later ones add more.
            var author = context.Set<Role>().AsNoTracking().Where(role => role.Id == BuiltInRoles.Author).Select(role => new { role.PermissionsVersion, role.Description }).Single();
            author.PermissionsVersion.ShouldBe(8);
            author.Description.ShouldNotBeNull().ShouldContain("reads");
            PermissionConstraints.FromJson(context.Set<RoleGrant>().AsNoTracking().Single(grant => grant.RoleId == BuiltInRoles.Author && grant.Permission == "content.*.read").Constraints).RowRule.ShouldBe("own-entries");

            migrator.Migrate(migrations[authorAt - 1]);

            context.Set<Role>().AsNoTracking().Where(role => role.Id == BuiltInRoles.Author).Select(role => role.PermissionsVersion).Single().ShouldBe(9);
            context.Set<RoleGrant>().AsNoTracking().Single(grant => grant.RoleId == BuiltInRoles.Author && grant.Permission == "content.*.read").Constraints.ShouldBeNull();
        }
    }

    [Fact]
    public void EverySeededGrantNamesSomethingInTheCatalogue()
    {
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        var catalogue = scope.ServiceProvider.GetRequiredService<PermissionCatalogue>();

        foreach (var grant in context.Set<RoleGrant>().AsNoTracking().ToList())
        {
            catalogue.IsGrantable(grant.Permission).ShouldBeTrue(grant.Permission);
        }
    }

    [Fact]
    public void ConstraintsRoundTripThroughTheDatabase()
    {
        var constraints = new PermissionConstraints
        {
            Sites = [new SiteId(SiteA), new SiteId(SiteB)],
            Locales = ["en", "fr"],
            Fields = ["title", "slug"],
            RowRule = "own-entries",
        };
        Guid grantId;

        using (var scope = _services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            var role = NewRole("Reviewer");
            context.Add(role);
            var grant = NewGrant(role.Id, "content.article.update", constraints);
            context.Add(grant);
            context.Add(NewGrant(role.Id, "content.article.read"));
            context.SaveChanges();
            grantId = grant.Id;
        }

        using (var scope = _services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            var stored = context.Set<RoleGrant>().AsNoTracking().Single(grant => grant.Id == grantId);
            var back = PermissionConstraints.FromJson(stored.Constraints);

            back.Sites.ShouldBe([new SiteId(SiteA), new SiteId(SiteB)]);
            back.Locales.ShouldBe(["en", "fr"]);
            back.Fields.ShouldBe(["title", "slug"]);
            back.RowRule.ShouldBe("own-entries");
            context.Set<RoleGrant>().AsNoTracking().Single(grant => grant.RoleId == stored.RoleId && grant.Permission == "content.article.read").Constraints.ShouldBeNull();
        }
    }

    [Fact]
    public void TheDatabaseRefusesMalformedConstraints()
    {
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        context.Add(new RoleGrant { RoleId = BuiltInRoles.Public, Permission = "content.article.read", Constraints = "{not json" });

        Should.Throw<DbUpdateException>(() => context.SaveChanges());
    }

    [Fact]
    public void ARoleGrantsUnionIsSeveralRowsForOnePermission()
    {
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        var role = NewRole("Regional");
        context.Add(role);
        context.SaveChanges();

        context.Add(NewGrant(role.Id, "content.article.read", new PermissionConstraints { Sites = [new SiteId(SiteA)] }));
        context.Add(NewGrant(role.Id, "content.article.read", new PermissionConstraints { Sites = [new SiteId(SiteB)] }));
        context.SaveChanges();

        GrantsOf(context, role.Id).ShouldBe(["content.article.read", "content.article.read"]);
    }

    [Fact]
    public void ANewRoleStartsAtVersionOne()
    {
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        var role = NewRole("Reviewer");
        context.Add(role);
        context.Add(NewGrant(role.Id, "content.article.read"));
        context.SaveChanges();

        VersionOf(context, role.Id).ShouldBe(1);
    }

    [Fact]
    public void AddingEditingAndRemovingAGrantEachRaiseTheVersionOfItsRole()
    {
        Guid roleId;
        using (var scope = _services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            var role = NewRole("Reviewer");
            context.Add(role);
            context.SaveChanges();
            roleId = role.Id;
        }

        Guid grantId;
        using (var scope = _services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();

            // Only the id of the role is known here, so the interceptor has to load it.
            var grant = NewGrant(roleId, "content.article.read");
            context.Add(grant);
            context.SaveChanges();
            grantId = grant.Id;

            VersionOf(context, roleId).ShouldBe(2);
        }

        using (var scope = _services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            context.Set<RoleGrant>().Single(grant => grant.Id == grantId).Constraints = new PermissionConstraints { Locales = ["en"] }.ToJson();
            context.SaveChanges();

            VersionOf(context, roleId).ShouldBe(3);
        }

        using (var scope = _services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            context.Remove(context.Set<RoleGrant>().Single(grant => grant.Id == grantId));
            context.SaveChanges();

            VersionOf(context, roleId).ShouldBe(4);
        }
    }

    [Fact]
    public async Task TheAsyncPathRaisesTheVersionToo()
    {
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();

        context.Add(NewGrant(BuiltInRoles.Member, "content.article.read"));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        VersionOf(context, BuiltInRoles.Member).ShouldBe(2);
    }

    [Fact]
    public void EditingTheRoleItselfRaisesItsVersion()
    {
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        var role = NewRole("Reviewer");
        context.Add(role);
        context.SaveChanges();

        role.Description = "Reads before publishing.";
        context.SaveChanges();

        VersionOf(context, role.Id).ShouldBe(2);
        role.PermissionsVersion.ShouldBe(2);
    }

    [Fact]
    public void SeveralChangesInOneSaveRaiseTheVersionOnce()
    {
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();

        context.Add(NewGrant(BuiltInRoles.Member, "content.article.read"));
        context.Add(NewGrant(BuiltInRoles.Member, "content.author.read"));
        context.Add(NewGrant(BuiltInRoles.Public, "content.tag.read"));
        context.SaveChanges();

        VersionOf(context, BuiltInRoles.Member).ShouldBe(2);
        VersionOf(context, BuiltInRoles.Public).ShouldBe(2);
        VersionOf(context, BuiltInRoles.Editor).ShouldBe(1);
    }

    [Fact]
    public void MovingAGrantToAnotherRoleRaisesBoth()
    {
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        var grant = NewGrant(BuiltInRoles.Member, "content.article.read");
        context.Add(grant);
        context.SaveChanges();

        grant.RoleId = BuiltInRoles.Public;
        context.SaveChanges();

        VersionOf(context, BuiltInRoles.Member).ShouldBe(3);
        VersionOf(context, BuiltInRoles.Public).ShouldBe(2);
    }

    [Fact]
    public void TwoWritersOfOneRoleCannotBothLandOnTheSameVersion()
    {
        using var first = _services.CreateScope();
        using var second = _services.CreateScope();
        var one = first.ServiceProvider.GetRequiredService<CmsDbContext>();
        var two = second.ServiceProvider.GetRequiredService<CmsDbContext>();

        one.Set<Role>().Find(BuiltInRoles.Member);
        two.Set<Role>().Find(BuiltInRoles.Member);
        one.Add(NewGrant(BuiltInRoles.Member, "content.article.read"));
        two.Add(NewGrant(BuiltInRoles.Member, "content.author.read"));

        one.SaveChanges();

        Should.Throw<DbUpdateConcurrencyException>(() => two.SaveChanges());
        VersionOf(one, BuiltInRoles.Member).ShouldBe(2);
    }

    [Fact]
    public void DeletingARoleDeletesItsGrants()
    {
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        var role = NewRole("Reviewer");
        context.Add(role);
        context.SaveChanges();
        context.Add(NewGrant(role.Id, "content.article.read"));
        context.SaveChanges();

        context.Remove(context.Set<Role>().Single(item => item.Id == role.Id));
        context.SaveChanges();

        context.Set<RoleGrant>().Count(grant => grant.RoleId == role.Id).ShouldBe(0);
    }

    [Fact]
    public void ANameIsUniqueAmongInstanceWideRolesAndWithinASite()
    {
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();

        context.Add(NewSite(SiteA));
        context.Add(NewSite(SiteB));
        context.SaveChanges();

        context.Add(NewRole("Public", SiteA));
        context.Add(NewRole("Public", SiteB));
        context.Add(NewRole("Reviewer"));
        context.Add(NewRole("Reviewer", SiteA));
        context.SaveChanges();

        context.Add(NewRole("Editor"));
        Should.Throw<DbUpdateException>(() => context.SaveChanges());
        context.ChangeTracker.Clear();

        context.Add(NewRole("Public", SiteA));
        Should.Throw<DbUpdateException>(() => context.SaveChanges());
    }
}
