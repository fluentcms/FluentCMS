using System.Security.Claims;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Events;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Events;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Identity.IdentityFixture;

namespace FluentCMS.Data.Tests.Identity;

public sealed class UserStoreTests : IDisposable
{
    private readonly IdentityFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task AUserWithItsRolesClaimsLoginsAndTokensRoundTrips()
    {
        var siteRole = _fixture.Database.Database.AddRoleForTest("Reviewer", Content.ServiceDatabase.SiteA.Value);
        var user = await _fixture.CreateUserAsync("ann", Password, BuiltInRoles.Editor, siteRole);
        await _fixture.UsersAsync(async users =>
        {
            var stored = (await users.FindByIdAsync(user.Id.ToString("D")))!;
            Succeeded(await users.AddToRoleAsync(stored, BuiltInRoles.AuthorName));
            Succeeded(await users.AddClaimAsync(stored, new Claim("region", "eu")));
            Succeeded(await users.AddLoginAsync(stored, new UserLoginInfo("entra", "ann-key", "Entra ID")));
            Succeeded(await users.SetAuthenticationTokenAsync(stored, "entra", "refresh", "opaque"));
            return true;
        });

        await _fixture.UsersAsync(async users =>
        {
            var read = (await users.FindByNameAsync("ANN")).ShouldNotBeNull();
            read.Id.ShouldBe(user.Id);
            read.Email.ShouldBe("ann@example.com");
            read.CreatedAt.ShouldBe(Start);
            (await users.FindByEmailAsync("Ann@Example.com")).ShouldNotBeNull().Id.ShouldBe(user.Id);
            (await users.CheckPasswordAsync(read, Password)).ShouldBeTrue();

            (await users.GetRoleIdsAsync(read)).ShouldBe([BuiltInRoles.Editor, siteRole, BuiltInRoles.Author], ignoreOrder: true);
            (await users.GetRolesAsync(read)).ShouldBe([BuiltInRoles.EditorName, BuiltInRoles.AuthorName], ignoreOrder: true);
            (await users.IsInRoleAsync(read, BuiltInRoles.AuthorName)).ShouldBeTrue();
            (await users.GetUsersInRoleAsync(BuiltInRoles.EditorName)).ShouldHaveSingleItem().Id.ShouldBe(user.Id);

            (await users.GetClaimsAsync(read)).ShouldHaveSingleItem().Value.ShouldBe("eu");
            (await users.FindByLoginAsync("entra", "ann-key")).ShouldNotBeNull().Id.ShouldBe(user.Id);
            (await users.GetAuthenticationTokenAsync(read, "entra", "refresh")).ShouldBe("opaque");

            Succeeded(await users.RemoveFromRoleAsync(read, siteRole));
            Succeeded(await users.RemoveFromRoleAsync(read, BuiltInRoles.AuthorName));
            return true;
        });

        (await _fixture.UsersAsync(async users => await users.GetRoleIdsAsync((await users.FindByIdAsync(user.Id.ToString("D")))!)))
            .ShouldBe([BuiltInRoles.Editor]);
    }

    // A provider of cms.Sso may be written in another case later; its logins stay its users' (#3010).
    [Fact]
    public async Task ALoginIsFoundUnderItsProviderInAnotherCaseAndNotTakenByAnotherUser()
    {
        var ann = await _fixture.CreateUserAsync("ann");
        var bob = await _fixture.CreateUserAsync("bob");
        await _fixture.UsersAsync(async users =>
        {
            Succeeded(await users.AddLoginAsync((await users.FindByIdAsync(ann.Id.ToString("D")))!, new UserLoginInfo("Corp", "ann-key", "Corp")));
            return true;
        });

        await _fixture.UsersAsync(async users =>
        {
            (await users.FindByLoginAsync("Corp", "ann-key")).ShouldNotBeNull().Id.ShouldBe(ann.Id);
            (await users.FindByLoginAsync("corp", "ann-key")).ShouldNotBeNull().Id.ShouldBe(ann.Id);
            (await users.FindByLoginAsync("CORP", "ann-key")).ShouldNotBeNull().Id.ShouldBe(ann.Id);
            (await users.FindByLoginAsync("corp-mfa", "ann-key")).ShouldBeNull();

            var taken = await users.AddLoginAsync((await users.FindByIdAsync(bob.Id.ToString("D")))!, new UserLoginInfo("corp", "ann-key", "Corp"));
            taken.Errors.ShouldHaveSingleItem().Code.ShouldBe(nameof(IdentityErrorDescriber.LoginAlreadyAssociated));
            return true;
        });
    }

    [Fact]
    public async Task ARoleIsGivenOnceAndARoleThatDoesNotExistIsRefused()
    {
        var user = await _fixture.CreateUserAsync("ann", Password, BuiltInRoles.Editor);

        await _fixture.UsersAsync(async users =>
        {
            var stored = (await users.FindByIdAsync(user.Id.ToString("D")))!;
            (await users.AddToRoleAsync(stored, BuiltInRoles.Editor)).Errors.ShouldHaveSingleItem().Code.ShouldBe(nameof(IdentityErrorDescriber.UserAlreadyInRole));
            (await users.AddToRolesAsync(stored, [BuiltInRoles.Author, Guid.CreateVersion7()])).Errors.ShouldHaveSingleItem().Code.ShouldBe(IdentityErrorCodes.UnknownRole);
            return true;
        });

        (await _fixture.UsersAsync(async users => await users.GetRoleIdsAsync((await users.FindByIdAsync(user.Id.ToString("D")))!)))
            .ShouldBe([BuiltInRoles.Editor]);
    }

    [Fact]
    public async Task APasswordIsStoredAsAnIdentityPbkdf2Hash()
    {
        var user = await _fixture.CreateUserAsync("ann");

        var hash = (await _fixture.FindAsync(user.Id)).PasswordHash.ShouldNotBeNull();
        hash.ShouldNotContain(Password);
        Convert.FromBase64String(hash)[0].ShouldBe((byte)0x01);
        await _fixture.WithAsync(services =>
        {
            services.GetRequiredService<IPasswordHasher<CmsUser>>().ShouldBeOfType<PasswordHasher<CmsUser>>();
            return Task.FromResult(true);
        });
    }

    [Fact]
    public async Task ThePasswordPolicyFollowsTheOptions()
    {
        using var strict = new IdentityFixture(cms =>
        {
            cms.Users.Password.RequiredLength = 20;
            cms.Users.Password.RequireNonAlphanumeric = false;
        });

        var refused = await strict.UsersAsync(users => users.CreateAsync(new CmsUser { UserName = "short", Email = "short@example.com" }, "Abcdefghij123456789"));
        refused.Errors.ShouldHaveSingleItem().Code.ShouldBe(nameof(IdentityErrorDescriber.PasswordTooShort));

        Succeeded(await strict.UsersAsync(users => users.CreateAsync(new CmsUser { UserName = "long", Email = "long@example.com" }, "Abcdefghij1234567890")));

        // The default asks for twelve characters, with Identity's other rules.
        var weak = await _fixture.UsersAsync(users => users.CreateAsync(new CmsUser { UserName = "weak", Email = "weak@example.com" }, "Abc-12345"));
        weak.Errors.Select(error => error.Code).ShouldBe([nameof(IdentityErrorDescriber.PasswordTooShort)]);
    }

    [Fact]
    public async Task LockoutFollowsTheOptions()
    {
        using var fixture = new IdentityFixture(cms =>
        {
            cms.Users.Lockout.MaxFailedAccessAttempts = 2;
            cms.Users.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(30);
        });

        var user = await fixture.CreateUserAsync("ann");
        await fixture.WithAsync(async services =>
        {
            var signIn = services.GetRequiredService<SignInManager<CmsUser>>();
            var stored = (await signIn.UserManager.FindByIdAsync(user.Id.ToString("D")))!;
            (await signIn.CheckPasswordSignInAsync(stored, "wrong", lockoutOnFailure: true)).IsLockedOut.ShouldBeFalse();
            (await signIn.CheckPasswordSignInAsync(stored, "wrong", lockoutOnFailure: true)).IsLockedOut.ShouldBeTrue();
            (await signIn.CheckPasswordSignInAsync(stored, Password, lockoutOnFailure: true)).IsLockedOut.ShouldBeTrue();
            return true;
        });

        var locked = await fixture.FindAsync(user.Id);
        locked.LockoutEnd.ShouldNotBeNull().ShouldBeInRange(DateTimeOffset.UtcNow.AddMinutes(28), DateTimeOffset.UtcNow.AddMinutes(30));
    }

    [Fact]
    public async Task AForcedPasswordChangeIsKeptUntilThePasswordChanges()
    {
        var user = await _fixture.CreateUserAsync("ann");
        await _fixture.UsersAsync(async users => (await users.RequirePasswordChangeAsync((await users.FindByIdAsync(user.Id.ToString("D")))!)).Succeeded);
        (await _fixture.FindAsync(user.Id)).MustChangePassword.ShouldBeTrue();

        var weak = await _fixture.UsersAsync(async users => await users.ChangePasswordAsync((await users.FindByIdAsync(user.Id.ToString("D")))!, Password, "short"));
        weak.Succeeded.ShouldBeFalse();
        (await _fixture.FindAsync(user.Id)).MustChangePassword.ShouldBeTrue();

        Succeeded(await _fixture.UsersAsync(async users => await users.ChangePasswordAsync((await users.FindByIdAsync(user.Id.ToString("D")))!, Password, "Another-horse-2")));
        (await _fixture.FindAsync(user.Id)).MustChangePassword.ShouldBeFalse();
    }

    [Fact]
    public async Task ADeactivatedUserCannotSignInHoldsNoRolesAndIsLoggedOnce()
    {
        using var fixture = new IdentityFixture(activity: true);
        var user = await fixture.CreateUserAsync("ann", Password, BuiltInRoles.Editor);
        var stamp = (await fixture.FindAsync(user.Id)).SecurityStamp;

        await fixture.WithAsync(async services =>
        {
            var signIn = services.GetRequiredService<SignInManager<CmsUser>>();
            var users = services.GetRequiredService<CmsUserManager>();
            var stored = (await users.FindByIdAsync(user.Id.ToString("D")))!;
            (await signIn.CanSignInAsync(stored)).ShouldBeTrue();

            Succeeded(await users.DeactivateAsync(stored));
            Succeeded(await users.DeactivateAsync(stored));
            return true;
        });

        await fixture.WithAsync(async services =>
        {
            var signIn = services.GetRequiredService<SignInManager<CmsUser>>();
            var stored = (await signIn.UserManager.FindByIdAsync(user.Id.ToString("D")))!;
            stored.DeactivatedAt.ShouldBe(Start);
            stored.SecurityStamp.ShouldNotBe(stamp);
            (await signIn.CanSignInAsync(stored)).ShouldBeFalse();
            (await signIn.CheckPasswordSignInAsync(stored, Password, lockoutOnFailure: true)).IsNotAllowed.ShouldBeTrue();
            return true;
        });

        (await fixture.RolesOfAsync(PrincipalOf(user))).ShouldBeEmpty();

        await fixture.WithAsync(services => services.GetRequiredService<OutboxDrainer>().DrainAsync(Token));
        var rows = fixture.ActivityRows();
        rows.Select(row => row.Action).ShouldBe(["user.created", "user.role_changed", "user.deactivated"], ignoreOrder: true);
        var deactivated = rows.Single(row => row.Action == "user.deactivated");
        deactivated.TargetType.ShouldBe("user");
        deactivated.TargetId.ShouldBe(user.Id.ToString("D"));
        deactivated.TargetLabel.ShouldBe("ann");
        deactivated.SiteId.ShouldBeNull();
        rows.Single(row => row.Action == "user.role_changed").Details.ShouldNotBeNull().ShouldContain(BuiltInRoles.Editor.ToString("D"));

        await fixture.UsersAsync(async users => (await users.ReactivateAsync((await users.FindByIdAsync(user.Id.ToString("D")))!)).Succeeded);
        (await fixture.RolesOfAsync(PrincipalOf(user))).ShouldBe([BuiltInRoles.Editor]);
    }

    [Fact]
    public async Task AChangeAnAdminMakesIsLoggedByNameAndTheCountersOfSignInAreNot()
    {
        using var fixture = new IdentityFixture(activity: true);
        var user = await fixture.CreateUserAsync("ann");

        await fixture.WithAsync(async services =>
        {
            var signIn = services.GetRequiredService<SignInManager<CmsUser>>();
            var stored = (await signIn.UserManager.FindByIdAsync(user.Id.ToString("D")))!;
            await signIn.CheckPasswordSignInAsync(stored, "wrong", lockoutOnFailure: true);
            Succeeded(await signIn.UserManager.SetEmailAsync(stored, "ann@example.org"));
            return true;
        });

        await fixture.WithAsync(services => services.GetRequiredService<OutboxDrainer>().DrainAsync(Token));
        var updated = fixture.ActivityRows().Where(row => row.Action == "user.updated").ShouldHaveSingleItem();
        updated.Details.ShouldNotBeNull().ShouldContain("email");
        updated.Details.ShouldNotContain("ann@example.org");
    }

    [Fact]
    public async Task ASiteRoleNamedLikeAnInstanceRoleIsNotNamedByIdentity()
    {
        var siteRole = _fixture.Database.Database.AddRoleForTest(BuiltInRoles.SuperAdminName, Content.ServiceDatabase.SiteA.Value);
        var user = await _fixture.CreateUserAsync("ann", Password, siteRole, BuiltInRoles.Editor);

        await _fixture.UsersAsync(async users =>
        {
            var stored = (await users.FindByIdAsync(user.Id.ToString("D")))!;
            (await users.GetRolesAsync(stored)).ShouldBe([BuiltInRoles.EditorName]);
            (await users.IsInRoleAsync(stored, BuiltInRoles.SuperAdminName)).ShouldBeFalse();
            (await users.GetUsersInRoleAsync(BuiltInRoles.SuperAdminName)).ShouldBeEmpty();
            (await users.GetRoleIdsAsync(stored)).ShouldBe([siteRole, BuiltInRoles.Editor], ignoreOrder: true);
            return true;
        });
    }

    [Fact]
    public async Task ARoleStagedByACallThatFailedIsNotSavedByTheNextSave()
    {
        var other = await _fixture.CreateUserAsync("other");
        var user = await _fixture.CreateUserAsync("ann");

        await _fixture.UsersAsync(async users =>
        {
            var stored = (await users.FindByIdAsync(user.Id.ToString("D")))!;
            var email = stored.Email;

            // The user validator refuses the save, after the role was staged.
            stored.Email = other.Email;
            (await users.AddToRoleAsync(stored, BuiltInRoles.SuperAdmin)).Succeeded.ShouldBeFalse();
            (await users.AddToRoleAsync(stored, BuiltInRoles.SuperAdminName)).Succeeded.ShouldBeFalse();
            (await users.AddClaimAsync(stored, new Claim("region", "eu"))).Succeeded.ShouldBeFalse();

            stored.Email = email;
            Succeeded(await users.SetPhoneNumberAsync(stored, "+100"));
            return true;
        });

        await _fixture.UsersAsync(async users =>
        {
            var stored = (await users.FindByIdAsync(user.Id.ToString("D")))!;
            stored.PhoneNumber.ShouldBe("+100");
            (await users.GetRoleIdsAsync(stored)).ShouldBeEmpty();
            (await users.GetClaimsAsync(stored)).ShouldBeEmpty();
            return true;
        });
    }

    [Fact]
    public async Task ARehashAtSignInIsNotLoggedAsAPasswordChangeAndAChangeIs()
    {
        using var fixture = new IdentityFixture(activity: true);
        var user = await fixture.CreateUserAsync("ann");

        // A hash of an older format, which a sign-in rehashes.
        var old = new PasswordHasher<CmsUser>(Options.Create(new PasswordHasherOptions { CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV2 })).HashPassword(user, Password);
        fixture.Read(context => context.Set<CmsUser>().Where(row => row.Id == user.Id).ExecuteUpdate(setters => setters.SetProperty(row => row.PasswordHash, old)));

        (await fixture.UsersAsync(async users => await users.CheckPasswordAsync((await users.FindByIdAsync(user.Id.ToString("D")))!, Password))).ShouldBeTrue();
        (await fixture.FindAsync(user.Id)).PasswordHash.ShouldNotBe(old);

        await fixture.WithAsync(services => services.GetRequiredService<OutboxDrainer>().DrainAsync(Token));
        fixture.ActivityRows().Where(row => row.Action == "user.updated").Select(row => row.Details).ShouldBeEmpty();

        Succeeded(await fixture.UsersAsync(async users => await users.ChangePasswordAsync((await users.FindByIdAsync(user.Id.ToString("D")))!, Password, "Another-horse-2")));
        await fixture.WithAsync(services => services.GetRequiredService<OutboxDrainer>().DrainAsync(Token));
        fixture.ActivityRows().Single(row => row.Action == "user.updated").Details.ShouldNotBeNull().ShouldContain(UserChanges.Password);
    }

    [Fact]
    public async Task APasswordAnAdminSetsKeepsTheForcedChange()
    {
        var user = await _fixture.CreateUserAsync("ann");
        await _fixture.UsersAsync(async users =>
        {
            var stored = (await users.FindByIdAsync(user.Id.ToString("D")))!;
            Succeeded(await users.RequirePasswordChangeAsync(stored));
            Succeeded(await users.RemovePasswordAsync(stored));
            Succeeded(await users.AddPasswordAsync(stored, "Temporary-horse-3"));
            return true;
        });

        (await _fixture.FindAsync(user.Id)).MustChangePassword.ShouldBeTrue();
    }

    [Fact]
    public async Task AUserIsDeactivatedNotDeleted()
    {
        var user = await _fixture.CreateUserAsync("ann");

        var result = await _fixture.UsersAsync(async users => await users.DeleteAsync((await users.FindByIdAsync(user.Id.ToString("D")))!));

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(IdentityErrorCodes.DeleteNotSupported);
        (await _fixture.FindAsync(user.Id)).ShouldNotBeNull();
    }

    [Fact]
    public async Task AUserHoldsTheRolesOfCmsUserRoleAndNotThoseOfItsClaim()
    {
        var user = await _fixture.CreateUserAsync("ann", Password, BuiltInRoles.Author);
        var claiming = new FluentCMS.Content.Principal(FluentCMS.Content.PrincipalKind.AdminUser, user.Id, "ann", new Dictionary<string, string> { [ClaimPrincipalRoles.Claim] = BuiltInRoles.SuperAdmin.ToString() });
        var key = new FluentCMS.Content.Principal(FluentCMS.Content.PrincipalKind.ApiKey, Guid.CreateVersion7(), "key", new Dictionary<string, string> { [ClaimPrincipalRoles.Claim] = BuiltInRoles.Editor.ToString() });
        var service = new FluentCMS.Content.Principal(FluentCMS.Content.PrincipalKind.ServiceAccount, Guid.CreateVersion7(), "ci", new Dictionary<string, string> { [ClaimPrincipalRoles.Claim] = BuiltInRoles.Editor.ToString() });

        (await _fixture.RolesOfAsync(claiming)).ShouldBe([BuiltInRoles.Author]);
        (await _fixture.RolesOfAsync(PrincipalOf(new CmsUser { UserName = "nobody" }))).ShouldBeEmpty();
        (await _fixture.RolesOfAsync(key)).ShouldBeEmpty();
        (await _fixture.RolesOfAsync(service)).ShouldBe([BuiltInRoles.Editor]);
    }

    [Fact]
    public async Task ADeletedRoleTakesItsMembershipsAlong()
    {
        var role = _fixture.Database.Database.AddRoleForTest("Temporary");
        var user = await _fixture.CreateUserAsync("ann", Password, role);

        _fixture.Read(context => context.Set<Role>().Where(item => item.Id == role).ExecuteDelete());

        (await _fixture.RolesOfAsync(PrincipalOf(user))).ShouldBeEmpty();
        _fixture.Read(context => context.Set<CmsUserRole>().Count()).ShouldBe(0);
    }
}
