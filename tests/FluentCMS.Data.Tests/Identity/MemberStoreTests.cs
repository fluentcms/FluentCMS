using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity.Members;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Authorization;
using FluentCMS.Data.Tests.Content;
using FluentCMS.Sites;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Identity.IdentityFixture;

namespace FluentCMS.Data.Tests.Identity;

// The member tables on every provider: Identity's store over them, what the database keeps unique and removes with a
// member, and the one query in which the evaluator reads a member's groups and roles.
public sealed class MemberStoreTests : IDisposable
{
    private static readonly AccessTarget InSiteA = new(ServiceDatabase.SiteA, null);
    private static readonly AccessTarget InSiteB = new(ServiceDatabase.SiteB, null);

    private readonly IdentityFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task AMemberWithItsLoginsAndTokensRoundTripsApartFromTheAdminUsers()
    {
        var member = await CreateMemberAsync("mia@example.com", Password);
        var admin = await _fixture.CreateUserAsync("mia");
        await MembersAsync(async members =>
        {
            var stored = (await members.FindByIdAsync(member.Id.ToString("D")))!;
            Succeeded(await members.AddLoginAsync(stored, new UserLoginInfo("google", "mia-key", "Google")));
            Succeeded(await members.SetAuthenticationTokenAsync(stored, "google", "refresh", "opaque"));
            return true;
        });

        // The same provider and key on an admin user is another row, of another table.
        await _fixture.UsersAsync(async users =>
        {
            Succeeded(await users.AddLoginAsync((await users.FindByIdAsync(admin.Id.ToString("D")))!, new UserLoginInfo("google", "mia-key", "Google")));
            return true;
        });

        await MembersAsync(async members =>
        {
            var read = (await members.FindByEmailAsync("Mia@Example.com")).ShouldNotBeNull();
            read.Id.ShouldBe(member.Id);
            read.CreatedAt.ShouldBe(Start);
            read.UserName.ShouldBe("mia@example.com");
            read.LockoutEnabled.ShouldBeTrue();
            read.TwoFactorEnabled.ShouldBeFalse();
            read.EmailConfirmed.ShouldBeFalse();
            (await members.FindByNameAsync("MIA@example.com")).ShouldNotBeNull().Id.ShouldBe(member.Id);
            (await members.FindByLoginAsync("google", "mia-key")).ShouldNotBeNull().Id.ShouldBe(member.Id);
            (await members.CheckPasswordAsync(read, Password)).ShouldBeTrue();
            (await members.GetLoginsAsync(read)).ShouldHaveSingleItem().ProviderDisplayName.ShouldBe("Google");
            (await members.GetAuthenticationTokenAsync(read, "google", "refresh")).ShouldBe("opaque");
            (await members.GetClaimsAsync(read)).ShouldBeEmpty();
            (await members.FindByEmailAsync("mia")).ShouldBeNull();
            return true;
        });

        _fixture.Read(context => context.Set<CmsMemberLogin>().Count()).ShouldBe(1);
        _fixture.Read(context => context.Set<IdentityUserLogin<Guid>>().Single().UserId).ShouldBe(admin.Id);
        (await _fixture.UsersAsync(users => users.FindByEmailAsync("mia@example.com"))).ShouldNotBeNull().Id.ShouldBe(admin.Id);
    }

    [Fact]
    public async Task AnAddressIsOneMemberAndADeletedMemberTakesItsLoginsTokensAndGroupsAlong()
    {
        var member = await CreateMemberAsync("sam@example.com", Password);
        var other = await CreateMemberAsync("ann@example.com", null);
        var group = AddGroup("early", BuiltInRoles.Member);
        AddToGroup(group, member.Id);
        AddToGroup(group, other.Id);
        await MembersAsync(async members =>
        {
            var stored = (await members.FindByIdAsync(member.Id.ToString("D")))!;
            Succeeded(await members.AddLoginAsync(stored, new UserLoginInfo("github", "sam-key", "GitHub")));
            Succeeded(await members.SetAuthenticationTokenAsync(stored, "github", "refresh", "opaque"));

            // The same address in another case is the same member, by the validator and by the index.
            (await members.CreateAsync(new CmsMember { Email = "SAM@example.com" })).Errors.ShouldContain(error => error.Code == nameof(IdentityErrorDescriber.DuplicateEmail));
            return true;
        });

        Should.Throw<DbUpdateException>(() => _fixture.Read(context =>
        {
            context.Add(new CmsMember { Email = "x@example.com", NormalizedEmail = "SAM@EXAMPLE.COM", CreatedAt = Start });
            return context.SaveChanges();
        }));
        Should.Throw<DbUpdateException>(() => AddGroup("early", BuiltInRoles.Member));
        Should.Throw<DbUpdateException>(() => AddToGroup(group, member.Id));

        await MembersAsync(async members =>
        {
            Succeeded(await members.DeleteAsync((await members.FindByIdAsync(member.Id.ToString("D")))!));
            return true;
        });

        _fixture.Read(context => context.Set<CmsMember>().Select(row => row.Id).ToList()).ShouldBe([other.Id]);
        _fixture.Read(context => context.Set<CmsMemberLogin>().Count()).ShouldBe(0);
        _fixture.Read(context => context.Set<CmsMemberToken>().Count()).ShouldBe(0);
        _fixture.Read(context => context.Set<MemberGroupMember>().Select(row => row.MemberId).ToList()).ShouldBe([other.Id]);

        // A group that is deleted takes its memberships along, and leaves its members.
        _fixture.Read(context => context.Set<MemberGroup>().Where(row => row.Id == group).ExecuteDelete()).ShouldBe(1);
        _fixture.Read(context => context.Set<MemberGroupMember>().Count()).ShouldBe(0);
        _fixture.Read(context => context.Set<CmsMember>().Count()).ShouldBe(1);
    }

    [Fact]
    public async Task TheEvaluatorGivesAMemberThePublicRoleTheMemberRoleAndTheRolesOfItsGroups()
    {
        var database = _fixture.Database.Database;
        database.GrantForTest(BuiltInRoles.Public, "content.bulletin.read");
        database.GrantForTest(BuiltInRoles.Member, "content.perk.read");
        var insider = database.AddRoleForTest("Insider", ("content.dossier.read", null));
        var local = database.AddRoleForTest("Local", ServiceDatabase.SiteB.Value, ("content.leaflet.read", null));
        var insiders = AddGroup("insiders", insider);
        var locals = AddGroup("locals", local);
        AddGroup("empty", insider);

        var plain = await CreateMemberAsync("plain@example.com", null);
        var grouped = await CreateMemberAsync("grouped@example.com", null);
        AddToGroup(insiders, grouped.Id);
        AddToGroup(locals, grouped.Id);

        var none = await PermissionsAsync(plain.Id, ServiceDatabase.SiteA);
        none.Allows("content.bulletin.read", InSiteA).ShouldBeTrue();
        none.Allows("content.perk.read", InSiteA).ShouldBeTrue();
        none.Allows("content.dossier.read", InSiteA).ShouldBeFalse();

        var inSiteA = await PermissionsAsync(grouped.Id, ServiceDatabase.SiteA);
        inSiteA.Allows("content.bulletin.read", InSiteA).ShouldBeTrue();
        inSiteA.Allows("content.perk.read", InSiteA).ShouldBeTrue();
        inSiteA.Allows("content.dossier.read", InSiteA).ShouldBeTrue();
        inSiteA.Allows("content.leaflet.read", InSiteA).ShouldBeFalse();

        // The role of a site holds in its site alone.
        var inSiteB = await PermissionsAsync(grouped.Id, ServiceDatabase.SiteB);
        inSiteB.Allows("content.leaflet.read", InSiteB).ShouldBeTrue();
        inSiteB.Allows("content.dossier.read", InSiteB).ShouldBeTrue();

        // Whatever a grant says, a member's token manages nothing, and a roles claim on a member is not read.
        database.GrantForTest(insider, "*");
        var managing = await PermissionsAsync(grouped.Id, ServiceDatabase.SiteA, new Dictionary<string, string> { [ClaimPrincipalRoles.Claim] = BuiltInRoles.SuperAdmin.ToString("D") });
        managing.CanManage.ShouldBeFalse();
        managing.Allows("content.anything.read", InSiteA).ShouldBeTrue();
        managing.Allows("content.anything.update", InSiteA).ShouldBeFalse();
        managing.Allows(CorePermissions.MembersManage, new AccessTarget(null, null)).ShouldBeFalse();
        var claimed = await PermissionsAsync(plain.Id, ServiceDatabase.SiteA, new Dictionary<string, string> { [ClaimPrincipalRoles.Claim] = BuiltInRoles.SuperAdmin.ToString("D") });
        claimed.Allows("content.anything.read", InSiteA).ShouldBeFalse();

        // A change holds from the next request: out of the group, and then deactivated.
        _fixture.Read(context => context.Set<MemberGroupMember>().Where(row => row.MemberId == grouped.Id && row.GroupId == insiders).ExecuteDelete()).ShouldBe(1);
        (await PermissionsAsync(grouped.Id, ServiceDatabase.SiteA)).Allows("content.dossier.read", InSiteA).ShouldBeFalse();

        await MembersAsync(async members =>
        {
            Succeeded(await ((MemberManager)members).DeactivateAsync((await members.FindByIdAsync(plain.Id.ToString("D")))!));
            return true;
        });
        var deactivated = await PermissionsAsync(plain.Id, ServiceDatabase.SiteA);
        deactivated.Allows("content.bulletin.read", InSiteA).ShouldBeFalse();
        deactivated.Allows("content.perk.read", InSiteA).ShouldBeFalse();
        (await PermissionsAsync(Guid.CreateVersion7(), ServiceDatabase.SiteA)).Allows("content.bulletin.read", InSiteA).ShouldBeFalse();
    }

    [Fact]
    public async Task ARoleIsNotDeletedWhileAMemberGroupIsBoundToIt()
    {
        var role = _fixture.Database.Database.AddRoleForTest("Bound", ("content.dossier.read", null));
        var group = AddGroup("bound", role);

        var refused = await Should.ThrowAsync<ValidationException>(() => _fixture.WithAsync(async services =>
        {
            await services.GetRequiredService<RoleService>().DeleteAsync(role, Token);
            return true;
        }));
        refused.Errors.ShouldHaveSingleItem().Code.ShouldBe("FCMS3044");

        _fixture.Read(context => context.Set<MemberGroup>().Where(row => row.Id == group).ExecuteDelete()).ShouldBe(1);
        await _fixture.WithAsync(async services =>
        {
            await services.GetRequiredService<RoleService>().DeleteAsync(role, Token);
            return true;
        });
        _fixture.Read(context => context.Set<Role>().Count(row => row.Id == role)).ShouldBe(0);
    }

    private Task<T> MembersAsync<T>(Func<UserManager<CmsMember>, Task<T>> work) =>
        _fixture.WithAsync(services => work(services.GetRequiredService<UserManager<CmsMember>>()));

    private async Task<CmsMember> CreateMemberAsync(string email, string? password)
    {
        var member = new CmsMember { Email = email };
        await MembersAsync(async members =>
        {
            Succeeded(password is null ? await members.CreateAsync(member) : await members.CreateAsync(member, password));
            return true;
        });
        return member;
    }

    private Guid AddGroup(string name, Guid role) => _fixture.Read(context =>
    {
        var group = new MemberGroup { Id = Guid.CreateVersion7(), Name = name, RoleId = role, CreatedAt = Start };
        context.Add(group);
        context.SaveChanges();
        return group.Id;
    });

    private void AddToGroup(Guid group, Guid member) => _fixture.Read(context =>
    {
        context.Add(new MemberGroupMember { GroupId = group, MemberId = member });
        return context.SaveChanges();
    });

    private async Task<EffectivePermissions> PermissionsAsync(Guid memberId, SiteId site, Dictionary<string, string>? claims = null)
    {
        var principal = new Principal(PrincipalKind.Member, memberId, "member", claims);
        using var scope = _fixture.Database.Database.NewScope();
        return await scope.ServiceProvider.GetRequiredService<IAuthorizationEvaluator>().ResolveAsync(AuthorizationDatabase.Operation(principal, Origin.Api, site), Token);
    }
}
