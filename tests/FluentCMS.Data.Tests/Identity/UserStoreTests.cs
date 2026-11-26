using System.Security.Claims;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Events;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Events;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Identity.IdentityFixture;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace FluentCMS.Data.Tests.Identity;

public sealed class UserStoreTests : IDisposable
{
    // A duplicate value that names the token table and its key, as SQL Server's message repeats it.
    private const string NamesTheTokenKey = "cms_user_token 'PK_cms_user_token'";

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
        using var fixture = new IdentityFixture(cms => cms.Sso.Providers["corp"] = new SsoProvider { Authority = "https://id.example.com", ClientId = "cms" });
        var ann = await fixture.CreateUserAsync("ann");
        var bob = await fixture.CreateUserAsync("bob");
        await fixture.UsersAsync(async users =>
        {
            var stored = (await users.FindByIdAsync(ann.Id.ToString("D")))!;
            Succeeded(await users.AddLoginAsync(stored, new UserLoginInfo("Corp", "ann-key", "Corp")));
            Succeeded(await users.AddLoginAsync(stored, new UserLoginInfo("Google", "ann-key", "Google")));
            return true;
        });

        await fixture.UsersAsync(async users =>
        {
            (await users.FindByLoginAsync("Corp", "ann-key")).ShouldNotBeNull().Id.ShouldBe(ann.Id);
            (await users.FindByLoginAsync("corp", "ann-key")).ShouldNotBeNull().Id.ShouldBe(ann.Id);
            (await users.FindByLoginAsync("CORP", "ann-key")).ShouldNotBeNull().Id.ShouldBe(ann.Id);
            (await users.FindByLoginAsync("corp-mfa", "ann-key")).ShouldBeNull();

            // A scheme that is no provider of cms.Sso is matched as it is written, as the database compares it.
            (await users.FindByLoginAsync("Google", "ann-key")).ShouldNotBeNull().Id.ShouldBe(ann.Id);
            if (!TestProvider.IsSqlServer)
            {
                (await users.FindByLoginAsync("google", "ann-key")).ShouldBeNull();
            }

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
        var lockedAt = fixture.Clock.GetUtcNow();
        await fixture.WithAsync(async services =>
        {
            var signIn = services.GetRequiredService<SignInManager<CmsUser>>();
            var stored = (await signIn.UserManager.FindByIdAsync(user.Id.ToString("D")))!;
            (await signIn.CheckPasswordSignInAsync(stored, "wrong", lockoutOnFailure: true)).IsLockedOut.ShouldBeFalse();
            (await signIn.CheckPasswordSignInAsync(stored, "wrong", lockoutOnFailure: true)).IsLockedOut.ShouldBeTrue();
            (await signIn.CheckPasswordSignInAsync(stored, Password, lockoutOnFailure: true)).IsLockedOut.ShouldBeTrue();
            return true;
        });

        // The lockout runs on the host's clock: still on at its end, and a tick later the right password signs in.
        (await fixture.FindAsync(user.Id)).LockoutEnd.ShouldBe(lockedAt + TimeSpan.FromMinutes(30));
        fixture.Clock.Advance(TimeSpan.FromMinutes(30));
        (await SignInAsync(fixture, user)).IsLockedOut.ShouldBeTrue();
        fixture.Clock.Advance(TimeSpan.FromTicks(1));
        (await SignInAsync(fixture, user)).Succeeded.ShouldBeTrue();

        static Task<SignInResult> SignInAsync(IdentityFixture fixture, CmsUser user) => fixture.WithAsync(async services =>
        {
            var signIn = services.GetRequiredService<SignInManager<CmsUser>>();
            return await signIn.CheckPasswordSignInAsync((await signIn.UserManager.FindByIdAsync(user.Id.ToString("D")))!, Password, lockoutOnFailure: true);
        });
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

    // A save with a new security stamp revokes the user's sessions and their tokens in its transaction; a save without one,
    // and another user's sessions, are left (#3202).
    [Fact]
    public async Task ANewSecurityStampRevokesTheUsersSessionsAndNothingElse()
    {
        var ann = await _fixture.CreateUserAsync("ann");
        var bob = await _fixture.CreateUserAsync("bob");
        var annSession = await SessionAsync(_fixture, ann);
        var bobSession = await SessionAsync(_fixture, bob);

        Succeeded(await _fixture.UsersAsync(async users => await users.RequirePasswordChangeAsync((await users.FindByIdAsync(ann.Id.ToString("D")))!)));
        (await StatusesAsync(_fixture, annSession)).ShouldBe([Statuses.Valid, Statuses.Valid]);

        Succeeded(await _fixture.UsersAsync(async users => await users.ChangePasswordAsync((await users.FindByIdAsync(ann.Id.ToString("D")))!, Password, "Another-horse-2")));

        (await StatusesAsync(_fixture, annSession)).ShouldBe([Statuses.Revoked, Statuses.Revoked]);
        (await StatusesAsync(_fixture, bobSession)).ShouldBe([Statuses.Valid, Statuses.Valid]);
    }

    // A sign-in that rehashes the password gives a new stamp too, so it ends the user's other sessions, as it ends their
    // cookies and refresh tokens.
    [Fact]
    public async Task ARehashAtSignInRevokesTheUsersSessions()
    {
        var user = await _fixture.CreateUserAsync("ann");
        var old = new PasswordHasher<CmsUser>(Options.Create(new PasswordHasherOptions { CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV2 })).HashPassword(user, Password);
        _fixture.Read(context => context.Set<CmsUser>().Where(row => row.Id == user.Id).ExecuteUpdate(setters => setters.SetProperty(row => row.PasswordHash, old)));
        var session = await SessionAsync(_fixture, user);

        (await _fixture.UsersAsync(async users => await users.CheckPasswordAsync((await users.FindByIdAsync(user.Id.ToString("D")))!, Password))).ShouldBeTrue();

        (await StatusesAsync(_fixture, session)).ShouldBe([Statuses.Revoked, Statuses.Revoked]);
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

    // A password an operator sets, as `user reset-password --password-stdin` does, gives a new stamp in the same save and
    // the forced change, since the user did not choose it.
    [Fact]
    public async Task APasswordAnOperatorSetsGivesANewStampAndTheForcedChange()
    {
        var user = await _fixture.CreateUserAsync("ann");
        var before = (await _fixture.FindAsync(user.Id)).SecurityStamp;

        (await _fixture.UsersAsync(async users => await users.SetPasswordAsync((await users.FindByIdAsync(user.Id.ToString("D")))!, "short"))).Succeeded.ShouldBeFalse();
        var refused = await _fixture.FindAsync(user.Id);
        refused.SecurityStamp.ShouldBe(before);
        refused.MustChangePassword.ShouldBeFalse();

        Succeeded(await _fixture.UsersAsync(async users => await users.SetPasswordAsync((await users.FindByIdAsync(user.Id.ToString("D")))!, "Temporary-horse-3")));
        var stored = await _fixture.FindAsync(user.Id);
        stored.MustChangePassword.ShouldBeTrue();
        stored.SecurityStamp.ShouldNotBe(before);
        (await _fixture.UsersAsync(users => users.CheckPasswordAsync(stored, "Temporary-horse-3"))).ShouldBeTrue();
    }

    [Fact]
    public async Task AWrongCurrentPasswordCountsTowardsTheLockoutAndARightOneStartsTheCountAgain()
    {
        var user = await _fixture.CreateUserAsync("ann");
        var attempts = new AdminUserOptions().Lockout.MaxFailedAccessAttempts;
        Task<IdentityResult> ChangeAsync(string current, string next) =>
            _fixture.UsersAsync(async users => await users.ChangePasswordAsync((await users.FindByIdAsync(user.Id.ToString("D")))!, current, next));

        (await ChangeAsync("wrong", "Another-horse-2")).Errors.ShouldHaveSingleItem().Code.ShouldBe(nameof(IdentityErrorDescriber.PasswordMismatch));
        Succeeded(await ChangeAsync(Password, "Another-horse-2"));
        (await _fixture.FindAsync(user.Id)).AccessFailedCount.ShouldBe(0);

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            (await ChangeAsync("wrong", "Third-horse-3")).Errors.ShouldHaveSingleItem().Code.ShouldBe(nameof(IdentityErrorDescriber.PasswordMismatch));
        }

        (await _fixture.UsersAsync(async users => await users.IsLockedOutAsync((await users.FindByIdAsync(user.Id.ToString("D")))!))).ShouldBeTrue();
        (await ChangeAsync("Another-horse-2", "Third-horse-3")).Errors.ShouldHaveSingleItem().Code.ShouldBe(CmsUserManager.LockedOutCode);
        var kept = await _fixture.FindAsync(user.Id);
        (await _fixture.UsersAsync(users => users.CheckPasswordAsync(kept, "Another-horse-2"))).ShouldBeTrue();
    }

    // A user of a provider only has no password to guess, so its attempts count nothing.
    [Fact]
    public async Task AChangeOfAUserWithoutAPasswordCountsNoFailure()
    {
        var user = await _fixture.CreateUserAsync("ann", password: null);

        for (var attempt = 0; attempt < new AdminUserOptions().Lockout.MaxFailedAccessAttempts; attempt++)
        {
            (await _fixture.UsersAsync(async users => await users.ChangePasswordAsync((await users.FindByIdAsync(user.Id.ToString("D")))!, "anything", "Another-horse-2"))).Succeeded.ShouldBeFalse();
        }

        var stored = await _fixture.FindAsync(user.Id);
        stored.AccessFailedCount.ShouldBe(0);
        stored.LockoutEnd.ShouldBeNull();
    }

    // A pair that collided before the check existed: neither user's other saves fail, the lockout among them, a rename into
    // the collision still does, and the startup check names the pair.
    [Fact]
    public async Task APairThatCollidedBeforeTheCheckKeepsItsOtherSavesAndIsReported()
    {
        var ann = await _fixture.CreateUserAsync("ann");
        var bob = await _fixture.CreateUserAsync("bob");
        _fixture.Read(context => context.Set<CmsUser>().Where(row => row.Id == bob.Id)
            .ExecuteUpdate(setters => setters.SetProperty(row => row.UserName, ann.Email).SetProperty(row => row.NormalizedUserName, ann.Email!.ToUpperInvariant())));

        foreach (var id in new[] { ann.Id, bob.Id })
        {
            Succeeded(await _fixture.UsersAsync(async users => await users.AccessFailedAsync((await users.FindByIdAsync(id.ToString("D")))!)));
            Succeeded(await _fixture.UsersAsync(async users => await users.ChangePasswordAsync((await users.FindByIdAsync(id.ToString("D")))!, Password, "Another-horse-2")));
        }

        (await _fixture.FindAsync(bob.Id)).AccessFailedCount.ShouldBe(0);
        await _fixture.WithAsync(async services =>
        {
            await new CrossNameStartupCheck(services.GetRequiredService<IServiceScopeFactory>(), services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CrossNameStartupCheck>>()).StartAsync(Token);
            return true;
        });
        _fixture.Logs.Messages.ShouldContain(message => message.Contains(bob.Id.ToString(), StringComparison.Ordinal) && message.Contains(ann.Id.ToString(), StringComparison.Ordinal));

        var renamed = await _fixture.UsersAsync(async users =>
        {
            var stored = (await users.FindByIdAsync(ann.Id.ToString("D")))!;
            stored.UserName = "carol@example.com";
            Succeeded(await users.UpdateAsync(stored));
            stored.Email = "bob-again@example.com";
            Succeeded(await users.UpdateAsync(stored));
            stored.UserName = bob.Email;
            return await users.UpdateAsync(stored);
        });
        renamed.Errors.ShouldHaveSingleItem().Code.ShouldBe(nameof(IdentityErrorDescriber.DuplicateUserName));

    }

    // Sign-in takes a name or an address, so neither may be another user's other one; a user's own name may be its address.
    [Fact]
    public async Task ANameOrAnAddressThatIsTheOtherOneOfAnotherUserIsRefused()
    {
        var ann = await _fixture.CreateUserAsync("ann");

        var name = await _fixture.UsersAsync(users => users.CreateAsync(new CmsUser { UserName = ann.Email!.ToUpperInvariant(), Email = "mallory@example.com" }, Password));
        name.Errors.ShouldHaveSingleItem().Code.ShouldBe(nameof(IdentityErrorDescriber.DuplicateUserName));

        Succeeded(await _fixture.UsersAsync(users => users.CreateAsync(new CmsUser { UserName = "alias@example.org", Email = "carol@example.com" }, Password)));
        var email = await _fixture.UsersAsync(users => users.CreateAsync(new CmsUser { UserName = "mallory", Email = "Alias@example.org" }, Password));
        email.Errors.ShouldHaveSingleItem().Code.ShouldBe(nameof(IdentityErrorDescriber.DuplicateEmail));

        Succeeded(await _fixture.UsersAsync(users => users.CreateAsync(new CmsUser { UserName = "dave@example.com", Email = "dave@example.com" }, Password)));
        Succeeded(await _fixture.UsersAsync(async users =>
        {
            var stored = (await users.FindByIdAsync(ann.Id.ToString("D")))!;
            stored.UserName = ann.Email;
            return await users.UpdateAsync(stored);
        }));
    }

    // Wrong passwords at sign-in, which anyone who knows the name can send, lock out new sign-ins only: a user who is signed
    // in still changes the password. What its own sessions get wrong is counted apart and keeps them out for the lockout's
    // time (#3371).
    [Fact]
    public async Task ALockoutDoesNotRefuseAPasswordChangeAndTheSessionsOwnWrongPasswordsDo()
    {
        var user = await _fixture.CreateUserAsync("ann");
        var lockout = new AdminUserOptions().Lockout;
        Task<T> AsAnnAsync<T>(Func<CmsUserManager, CmsUser, Task<T>> work) =>
            _fixture.UsersAsync(async users => await work(users, (await users.FindByIdAsync(user.Id.ToString("D")))!));

        for (var attempt = 0; attempt < lockout.MaxFailedAccessAttempts; attempt++)
        {
            Succeeded(await AsAnnAsync((users, ann) => users.AccessFailedAsync(ann)));
        }

        (await AsAnnAsync((users, ann) => users.IsLockedOutAsync(ann))).ShouldBeTrue();
        (await AsAnnAsync((users, ann) => users.IsSignedInLockedOutAsync(ann))).ShouldBeFalse();
        Succeeded(await AsAnnAsync((users, ann) => users.ChangePasswordAsync(ann, Password, "Another-horse-2")));
        (await AsAnnAsync((users, ann) => users.IsLockedOutAsync(ann))).ShouldBeTrue();

        for (var attempt = 0; attempt < lockout.MaxFailedAccessAttempts; attempt++)
        {
            (await AsAnnAsync((users, ann) => users.ChangePasswordAsync(ann, "wrong", "Third-horse-3"))).Errors.ShouldHaveSingleItem().Code.ShouldBe(nameof(IdentityErrorDescriber.PasswordMismatch));
        }

        (await AsAnnAsync((users, ann) => users.IsSignedInLockedOutAsync(ann))).ShouldBeTrue();
        (await AsAnnAsync((users, ann) => users.ChangePasswordAsync(ann, "Another-horse-2", "Third-horse-3"))).Errors.ShouldHaveSingleItem().Code.ShouldBe(CmsUserManager.LockedOutCode);

        _fixture.Clock.Advance(lockout.DefaultLockoutTimeSpan + TimeSpan.FromSeconds(1));
        Succeeded(await AsAnnAsync((users, ann) => users.ChangePasswordAsync(ann, "Another-horse-2", "Third-horse-3")));
        (await AsAnnAsync((users, ann) => users.GetAuthenticationTokenAsync(ann, "[AspNetUserStore]", "SignedInFailures"))).ShouldBeNull();
    }

    // The first token row of a name, added by two saves at once: the second's INSERT meets the first's on the key of the
    // token table while the user's stamp is as the save read it, and that is a concurrency failure like the stamp's. The
    // user is tracked again afterwards, so the next save of the scope goes through (#3381).
    [Fact]
    public async Task ASaveThatMeetsAnotherOnTheKeyOfATokenRowIsAConcurrencyFailure()
    {
        var user = await _fixture.CreateUserAsync("ann");

        var saved = await _fixture.WithAsync(async services =>
        {
            var users = services.GetRequiredService<CmsUserManager>();
            var tokens = (IUserAuthenticationTokenStore<CmsUser>)services.GetRequiredService<IUserStore<CmsUser>>();
            var ann = (await users.FindByIdAsync(user.Id.ToString("D")))!;
            await tokens.SetTokenAsync(ann, "entra", "refresh", "mine", Token);
            _fixture.Read(context =>
            {
                context.Add(new IdentityUserToken<Guid> { UserId = user.Id, LoginProvider = "entra", Name = "refresh", Value = "theirs" });
                return context.SaveChanges();
            });

            var result = await users.UpdateAsync(ann);
            Succeeded(await users.AccessFailedAsync(ann));
            return result;
        });

        saved.Errors.ShouldHaveSingleItem().Code.ShouldBe(nameof(IdentityErrorDescriber.ConcurrencyFailure));
        var stored = await _fixture.FindAsync(user.Id);
        stored.AccessFailedCount.ShouldBe(1);
        (await _fixture.UsersAsync(users => users.GetAuthenticationTokenAsync(stored, "entra", "refresh"))).ShouldBe("theirs");
    }

    // Another unique key the same save breaks is not that race, so it is thrown and not answered as one (#3381), even when
    // its duplicate value names the token table and its key, which SQL Server's message repeats (#3447).
    [Fact]
    public async Task AnotherKeyBrokenByASaveThatAddsATokenRowIsThrown()
    {
        var ann = await _fixture.CreateUserAsync("ann");
        var bob = await _fixture.CreateUserAsync("bob");

        await Should.ThrowAsync<DbUpdateException>(() => _fixture.WithAsync(async services =>
        {
            var users = services.GetRequiredService<CmsUserManager>();
            var store = services.GetRequiredService<IUserStore<CmsUser>>();
            var stored = (await users.FindByIdAsync(ann.Id.ToString("D")))!;
            await ((IUserAuthenticationTokenStore<CmsUser>)store).SetTokenAsync(stored, "entra", "refresh", "mine", Token);
            await ((IUserLoginStore<CmsUser>)store).AddLoginAsync(stored, new UserLoginInfo("entra", NamesTheTokenKey, "Entra ID"), Token);
            _fixture.Read(context =>
            {
                context.Add(new IdentityUserLogin<Guid> { UserId = bob.Id, LoginProvider = "entra", ProviderKey = NamesTheTokenKey, ProviderDisplayName = "Entra ID" });
                return context.SaveChanges();
            });

            return await users.UpdateAsync(stored);
        }));

        (await _fixture.UsersAsync(async users => await users.GetAuthenticationTokenAsync((await users.FindByIdAsync(ann.Id.ToString("D")))!, "entra", "refresh"))).ShouldBeNull();
    }

    // The token table's key is told by the key the provider reports, never by a duplicate value that names the table or the
    // key, in a message of another language too (#3447). The errors of PostgreSQL and SQL Server are built as their drivers
    // build them, with messages that are predictions of a server's; the German ones are built here, not taken from a server.
    [Fact]
    public async Task TheTokenKeyIsToldByTheKeyTheProviderReportsAndNotByADuplicateValue()
    {
        var sqlServer = new FluentCMS.Data.SqlServer.SqlServerDataProvider("Server=localhost;Database=never_opened");
        var postgreSql = new FluentCMS.Data.PostgreSql.PostgreSqlDataProvider("Host=localhost;Database=never_opened");
        static DbUpdateException Saved(Exception inner) => new("An error occurred while saving the entity changes.", inner);

        await _fixture.WithAsync(services =>
        {
            var store = (CmsUserStore)services.GetRequiredService<IUserStore<CmsUser>>();
            store.IsTokenKeyViolation(sqlServer, Saved(UniqueViolationTests.SqlServerError(2627, "Violation of PRIMARY KEY constraint 'PK_cms_user_token'. Cannot insert duplicate key in object 'dbo.cms_user_token'. The duplicate key value is (u, p, n)."))).ShouldBeTrue();
            store.IsTokenKeyViolation(sqlServer, Saved(UniqueViolationTests.SqlServerError(2627, $"Violation of PRIMARY KEY constraint 'PK_cms_user_login'. Cannot insert duplicate key in object 'dbo.cms_user_login'. The duplicate key value is (entra, {NamesTheTokenKey})."))).ShouldBeFalse();
            store.IsTokenKeyViolation(sqlServer, Saved(UniqueViolationTests.SqlServerError(2627, "Verletzung der PRIMARY KEY-Einschränkung 'PK_cms_user_token'. Ein doppelter Schlüssel kann in das dbo.cms_user_token-Objekt nicht eingefügt werden. Der doppelte Schlüsselwert ist (u, p, n)."))).ShouldBeTrue();
            store.IsTokenKeyViolation(sqlServer, Saved(UniqueViolationTests.SqlServerError(2627, $"Verletzung der PRIMARY KEY-Einschränkung 'PK_cms_user_login'. Ein doppelter Schlüssel kann in das dbo.cms_user_login-Objekt nicht eingefügt werden. Der doppelte Schlüsselwert ist (entra, {NamesTheTokenKey})."))).ShouldBeFalse();
            store.IsTokenKeyViolation(postgreSql, Saved(UniqueViolationTests.PostgreSqlError(Npgsql.PostgresErrorCodes.UniqueViolation, "PK_cms_user_token", "Key (user_id, login_provider, name)=(u, p, n) already exists."))).ShouldBeTrue();
            store.IsTokenKeyViolation(postgreSql, Saved(UniqueViolationTests.PostgreSqlError(Npgsql.PostgresErrorCodes.UniqueViolation, "PK_cms_user_login", $"Key (login_provider, provider_key)=(entra, {NamesTheTokenKey}) already exists."))).ShouldBeFalse();
            return Task.FromResult(true);
        });
    }

    // A password change whose save lost to another request's reloads the user and is made again on what is stored: a right
    // password is then taken, and a wrong one counted on the count the other request stored, not on the one the change
    // remembered (#3381, #3447).
    [Fact]
    public async Task APasswordChangeWhoseSaveLostIsMadeAgainOnWhatAnotherRequestStored()
    {
        var user = await _fixture.CreateUserAsync("ann");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            Succeeded(await _fixture.UsersAsync(async users => await users.AccessFailedAsync((await users.FindByIdAsync(user.Id.ToString("D")))!)));
        }

        Task<IdentityResult> AfterAnotherSaveAsync(string current) => _fixture.UsersAsync(async users =>
        {
            var ann = (await users.FindByIdAsync(user.Id.ToString("D")))!;
            ann.AccessFailedCount.ShouldBeGreaterThan(0);
            _fixture.Read(context => context.Set<CmsUser>().Where(row => row.Id == user.Id)
                .ExecuteUpdate(setters => setters.SetProperty(row => row.AccessFailedCount, 0).SetProperty(row => row.ConcurrencyStamp, Guid.NewGuid().ToString())));

            var result = await users.ChangePasswordAsync(ann, current, "Another-horse-2");
            Succeeded(await users.UpdateAsync(ann));
            return result;
        });

        (await AfterAnotherSaveAsync("wrong")).Errors.ShouldHaveSingleItem().Code.ShouldBe(nameof(IdentityErrorDescriber.PasswordMismatch));
        (await _fixture.FindAsync(user.Id)).AccessFailedCount.ShouldBe(1);
        (await _fixture.UsersAsync(async users => await users.GetAuthenticationTokenAsync((await users.FindByIdAsync(user.Id.ToString("D")))!, "[AspNetUserStore]", "SignedInFailures"))).ShouldBe("1:");

        Succeeded(await AfterAnotherSaveAsync(Password));
        var stored = await _fixture.FindAsync(user.Id);
        stored.AccessFailedCount.ShouldBe(0);
        (await _fixture.UsersAsync(users => users.CheckPasswordAsync(stored, "Another-horse-2"))).ShouldBeTrue();
    }

    // Wrong current passwords a session sends at once all read the user before any of them saved. Each is counted, one that
    // lost its save on what is stored then, so that they count as many as they are, and those past the limit are refused
    // unchecked. The lockout counts them too (#3447).
    [Fact]
    public async Task WrongPasswordsSentAtOnceAreEachCountedAndThoseAfterTheLimitRefused()
    {
        var user = await _fixture.CreateUserAsync("ann");
        var lockout = new AdminUserOptions().Lockout;
        var gate = new Gate(lockout.MaxFailedAccessAttempts + 2);

        var answers = await Task.WhenAll(Enumerable.Range(0, gate.Count).Select(_ => _fixture.UsersAsync(async users =>
        {
            var ann = (await users.FindByIdAsync(user.Id.ToString("D")))!;
            await gate.PassAsync();
            return (await users.ChangePasswordAsync(ann, "wrong", "Another-horse-2")).Errors.ShouldHaveSingleItem().Code;
        }))).WaitAsync(TimeSpan.FromMinutes(1), Token);

        answers.Count(code => code == nameof(IdentityErrorDescriber.PasswordMismatch)).ShouldBe(lockout.MaxFailedAccessAttempts, string.Join(", ", answers));
        answers.Count(code => code == CmsUserManager.LockedOutCode).ShouldBe(2, string.Join(", ", answers));
        (await _fixture.UsersAsync(async users => await users.IsSignedInLockedOutAsync((await users.FindByIdAsync(user.Id.ToString("D")))!))).ShouldBeTrue();
        (await _fixture.UsersAsync(async users => await users.IsLockedOutAsync((await users.FindByIdAsync(user.Id.ToString("D")))!))).ShouldBeTrue();
        (await _fixture.UsersAsync(async users => await users.ChangePasswordAsync((await users.FindByIdAsync(user.Id.ToString("D")))!, Password, "Another-horse-2")))
            .Errors.ShouldHaveSingleItem().Code.ShouldBe(CmsUserManager.LockedOutCode);
        var stored = await _fixture.FindAsync(user.Id);
        (await _fixture.UsersAsync(users => users.CheckPasswordAsync(stored, Password))).ShouldBeTrue();
    }

    // A right password sent with wrong ones read the user before any of them saved, and loses its save to the first wrong
    // one. It is not answered with that failure, which would tell the sender it was right: it is made again and taken, and
    // every wrong one is answered as wrong (#3447).
    [Fact]
    public async Task ARightPasswordThatLostItsSaveToAWrongOneIsTakenOnItsNextTry()
    {
        var user = await _fixture.CreateUserAsync("ann");
        var wrongs = new AdminUserOptions().Lockout.MaxFailedAccessAttempts - 1;
        var gate = new Gate(wrongs + 1);
        Task<string> GuessAsync(string current, Func<Task> after) => _fixture.UsersAsync(async users =>
        {
            var ann = (await users.FindByIdAsync(user.Id.ToString("D")))!;
            await gate.PassAsync();
            await after().WaitAsync(TimeSpan.FromSeconds(20), Token);
            var result = await users.ChangePasswordAsync(ann, current, "Another-horse-2");
            return result.Succeeded ? "" : result.Errors.ShouldHaveSingleItem().Code;
        });

        List<Task<string>> wrong = [.. Enumerable.Range(0, wrongs).Select(_ => GuessAsync("wrong", () => Task.CompletedTask))];
        var right = GuessAsync(Password, () => Task.WhenAny(wrong));

        (await right.WaitAsync(TimeSpan.FromMinutes(1), Token)).ShouldBe("");
        (await Task.WhenAll(wrong).WaitAsync(TimeSpan.FromMinutes(1), Token)).ShouldAllBe(code => code == nameof(IdentityErrorDescriber.PasswordMismatch));
        var stored = await _fixture.FindAsync(user.Id);
        (await _fixture.UsersAsync(users => users.CheckPasswordAsync(stored, "Another-horse-2"))).ShouldBeTrue();
    }

    // Saves that keep losing, as under a flood of other saves of the user. A guess whose place in the count is never saved
    // is never checked: it ends with the concurrency failure after the same saves whether it was right or wrong. A guess
    // that holds its place hashes the new password and is checked, once each: a wrong one is answered then, and a right one
    // keeps the hash for every try of its save, which lost to no other password, and when they run out it is answered as a
    // wrong one, its place counted. The answers, the saves and the work are the same (#3447, #3601).
    [Fact]
    public async Task AChangeWhoseSavesKeepLosingAnswersTheSameForARightAndAWrongPassword()
    {
        var losing = new LosingSaves();
        var hasher = new CountingHasher();
        using var fixture = new IdentityFixture(cms =>
        {
            cms.Services.AddSingleton<IPasswordHasher<CmsUser>>(hasher);
            cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(losing));
        });
        var user = await fixture.CreateUserAsync("ann");
        var tries = 2 * new AdminUserOptions().Lockout.MaxFailedAccessAttempts;
        async Task<(string Code, int Saves, int Checks, int Hashes)> ChangeAsync(string current, int passing)
        {
            hasher.Clear();
            losing.Arm(passing);
            var result = await fixture.UsersAsync(async users => await users.ChangePasswordAsync((await users.FindByIdAsync(user.Id.ToString("D")))!, current, "Another-horse-2"));
            return (result.Errors.ShouldHaveSingleItem().Code, losing.Disarm(), hasher.Verified.Count, hasher.Hashed);
        }

        var right = await ChangeAsync(Password, passing: 0);
        var wrong = await ChangeAsync("wrong", passing: 0);
        right.ShouldBe((nameof(IdentityErrorDescriber.ConcurrencyFailure), tries, 0, 0));
        wrong.ShouldBe(right);

        right = await ChangeAsync(Password, passing: 1);
        wrong = await ChangeAsync("wrong", passing: 1);
        right.ShouldBe((nameof(IdentityErrorDescriber.PasswordMismatch), 1 + tries, 1, 1));
        wrong.ShouldBe(right);

        var stored = await fixture.FindAsync(user.Id);
        stored.AccessFailedCount.ShouldBe(0);
        (await fixture.UsersAsync(users => users.GetAuthenticationTokenAsync(stored, "[AspNetUserStore]", "SignedInFailures"))).ShouldBe("2:");
        (await fixture.UsersAsync(users => users.CheckPasswordAsync(stored, Password))).ShouldBeTrue();
    }

    // A right and a wrong current password each make the same key derivations, one hash of the new password and one check,
    // so that the time of the answer tells them apart no more than the answer does (#3601).
    [Fact]
    public async Task ARightAndAWrongCurrentPasswordDoTheSameKeyDerivations()
    {
        var hasher = new CountingHasher();
        using var fixture = new IdentityFixture(cms => cms.Services.AddSingleton<IPasswordHasher<CmsUser>>(hasher));
        var user = await fixture.CreateUserAsync("ann");
        async Task<(string Answer, int Derivations)> ChangeAsync(string current)
        {
            hasher.Clear();
            var result = await fixture.UsersAsync(async users => await users.ChangePasswordAsync((await users.FindByIdAsync(user.Id.ToString("D")))!, current, "Another-horse-2"));
            return (result.Succeeded ? "" : result.Errors.ShouldHaveSingleItem().Code, hasher.Verified.Count + hasher.Hashed);
        }

        var wrong = await ChangeAsync("wrong");
        var right = await ChangeAsync(Password);

        wrong.ShouldBe((nameof(IdentityErrorDescriber.PasswordMismatch), 2));
        right.ShouldBe(("", 2));
        (await fixture.UsersAsync(async users => await users.CheckPasswordAsync((await users.FindByIdAsync(user.Id.ToString("D")))!, "Another-horse-2"))).ShouldBeTrue();
    }

    // A right current password whose save lost is checked again only when the save lost to another password. Its save made
    // again over another save of the user keeps the one hash and check it made; over a password an operator set meanwhile,
    // the guess is checked against that one, and answered and counted as a wrong one (#3601).
    [Fact]
    public async Task ACurrentPasswordIsCheckedAgainOnlyWhenTheSaveLostToAnotherPassword()
    {
        var hasher = new CountingHasher();
        using var fixture = new IdentityFixture(cms => cms.Services.AddSingleton<IPasswordHasher<CmsUser>>(hasher));
        var user = await fixture.CreateUserAsync("ann");
        var operators = new PasswordHasher<CmsUser>().HashPassword(new CmsUser(), "Operator-horse-3");
        async Task<(string Answer, int Checks, int Hashes)> AfterAnotherSaveAsync(string? hash)
        {
            hasher.Clear();
            hasher.AfterNextCheck(() => fixture.Read(context => context.Set<CmsUser>().Where(row => row.Id == user.Id).ExecuteUpdate(setters => setters
                .SetProperty(row => row.PasswordHash, row => hash ?? row.PasswordHash)
                .SetProperty(row => row.ConcurrencyStamp, Guid.NewGuid().ToString()))));
            var result = await fixture.UsersAsync(async users => await users.ChangePasswordAsync((await users.FindByIdAsync(user.Id.ToString("D")))!, Password, "Another-horse-2"));
            return (result.Succeeded ? "" : result.Errors.ShouldHaveSingleItem().Code, hasher.Verified.Count, hasher.Hashed);
        }

        (await AfterAnotherSaveAsync(null)).ShouldBe(("", 1, 1));
        Succeeded(await fixture.UsersAsync(async users => await users.SetPasswordAsync((await users.FindByIdAsync(user.Id.ToString("D")))!, Password)));
        (await AfterAnotherSaveAsync(operators)).ShouldBe((nameof(IdentityErrorDescriber.PasswordMismatch), 2, 1));

        var stored = await fixture.FindAsync(user.Id);
        stored.PasswordHash.ShouldBe(operators);
        stored.AccessFailedCount.ShouldBe(1);
        (await fixture.UsersAsync(users => users.GetAuthenticationTokenAsync(stored, "[AspNetUserStore]", "SignedInFailures"))).ShouldBe("1:");
    }

    // Far more guesses than the tries of a save, sent at once with the right one among them. A guess takes its place in the
    // count before it is checked, so no more than the limit are checked against the password, whatever the new password: one
    // the policy refuses is refused before any guess is checked, so its answer never depends on the guess. Only a checked
    // guess can have an answer that depends on it (#3447).
    [Theory]
    [InlineData("a")]
    [InlineData("Another-horse-2")]
    public async Task GuessesFarAboveTheTriesSentAtOnceAreCheckedNoMoreThanTheLimit(string next)
    {
        var hasher = new CountingHasher();
        using var fixture = new IdentityFixture(cms => cms.Services.AddSingleton<IPasswordHasher<CmsUser>>(hasher));
        var user = await fixture.CreateUserAsync("ann");
        var original = (await fixture.FindAsync(user.Id)).PasswordHash!;
        var limit = new AdminUserOptions().Lockout.MaxFailedAccessAttempts;
        List<string> guesses = [.. Enumerable.Range(0, 15).Select(index => $"wrong-{index}"), Password, .. Enumerable.Range(15, 15).Select(index => $"wrong-{index}")];
        var gate = new Gate(guesses.Count);

        var answers = await Task.WhenAll(guesses.Select(guess => fixture.UsersAsync(async users =>
        {
            var ann = (await users.FindByIdAsync(user.Id.ToString("D")))!;
            await gate.PassAsync();
            var result = await users.ChangePasswordAsync(ann, guess, next);
            return (Guess: guess, Answer: result.Succeeded ? "" : string.Join(" ", result.Errors.Select(error => error.Code)));
        }))).WaitAsync(TimeSpan.FromMinutes(2), Token);

        var checkedGuesses = hasher.Verified.Where(verified => verified.Hash == original).Select(verified => verified.Provided).ToHashSet();
        checkedGuesses.Count.ShouldBeLessThanOrEqualTo(limit, string.Join(", ", answers));
        string[] refusals = next == "a"
            ? [answers[0].Answer]
            : [CmsUserManager.LockedOutCode, nameof(IdentityErrorDescriber.ConcurrencyFailure)];
        answers.Where(answer => !checkedGuesses.Contains(answer.Guess)).ShouldAllBe(answer => refusals.Contains(answer.Answer), string.Join(", ", answers));
        if (next == "a")
        {
            checkedGuesses.ShouldBeEmpty();
            answers[0].Answer.ShouldContain(nameof(IdentityErrorDescriber.PasswordTooShort));
        }
        else
        {
            // The first place saved is a guess's, which is then checked.
            checkedGuesses.ShouldNotBeEmpty(string.Join(", ", answers));
        }
    }

    // Codes sent at once are counted as passwords are, apart from them: each wrong one counts, and those past the limit are
    // refused with the same answer (#3447).
    [Fact]
    public async Task WrongCodesSentAtOnceAreEachCountedApartFromPasswords()
    {
        var user = await _fixture.CreateUserAsync("ann");
        var lockout = new AdminUserOptions().Lockout;
        await _fixture.UsersAsync(async users =>
        {
            var ann = (await users.FindByIdAsync(user.Id.ToString("D")))!;
            Succeeded(await users.SetPendingAuthenticatorKeyAsync(ann));
            Succeeded(await users.SetTwoFactorEnabledAsync(ann, true));
            return true;
        });
        var gate = new Gate(lockout.MaxFailedAccessAttempts + 2);

        var answers = await Task.WhenAll(Enumerable.Range(0, gate.Count).Select(_ => _fixture.WithAsync(async services =>
        {
            var ann = (await services.GetRequiredService<CmsUserManager>().FindByIdAsync(user.Id.ToString("D")))!;
            await gate.PassAsync();
            var refused = await Should.ThrowAsync<FluentCMS.Content.ValidationException>(() => services.GetRequiredService<TwoFactorService>().RegenerateRecoveryCodesAsync(ann, "not-a-code"));
            return refused.Errors.ShouldHaveSingleItem().Code;
        }))).WaitAsync(TimeSpan.FromMinutes(1), Token);

        answers.ShouldAllBe(code => code == IdentityErrorCodes.InvalidTwoFactorCode);
        var stored = await _fixture.FindAsync(user.Id);
        (await _fixture.UsersAsync(users => users.IsSignedInLockedOutAsync(stored))).ShouldBeTrue();
        (await _fixture.UsersAsync(users => users.IsLockedOutAsync(stored))).ShouldBeTrue();
        (await _fixture.UsersAsync(users => users.GetAuthenticationTokenAsync(stored, "[AspNetUserStore]", "SignedInCodeFailures"))).ShouldNotBeNull().ShouldStartWith("0:");
        (await _fixture.UsersAsync(users => users.GetAuthenticationTokenAsync(stored, "[AspNetUserStore]", "SignedInFailures"))).ShouldBeNull();
    }

    // Far more codes than the tries of a save, sent at once with the right one among them. A code takes its place in the
    // count before it is checked, so no more than the limit are checked before a right one is taken, and a right one is
    // answered only once the save that takes it went through. A right one starts the count of codes again, by design, so
    // codes still in flight then may take places of the new count (#3447).
    [Fact]
    public async Task CodesFarAboveTheTriesSentAtOnceAreCheckedNoMoreThanTheLimit()
    {
        var counting = new CountingAuthenticator();
        using var fixture = new IdentityFixture(cms => cms.Services.PostConfigure<IdentityOptions>(options =>
        {
            counting.Inner = options.Tokens.ProviderMap[options.Tokens.AuthenticatorTokenProvider].ProviderInstance as IUserTwoFactorTokenProvider<CmsUser>;
            options.Tokens.ProviderMap[options.Tokens.AuthenticatorTokenProvider] = new TokenProviderDescriptor(typeof(CountingAuthenticator)) { ProviderInstance = counting };
        }));
        counting.Clock = fixture.Clock;
        var user = await fixture.CreateUserAsync("ann");
        var limit = new AdminUserOptions().Lockout.MaxFailedAccessAttempts;
        var key = await fixture.UsersAsync(async users =>
        {
            var ann = (await users.FindByIdAsync(user.Id.ToString("D")))!;
            Succeeded(await users.SetPendingAuthenticatorKeyAsync(ann));
            Succeeded(await users.SetTwoFactorEnabledAsync(ann, true));
            return (await users.GetAuthenticatorKeyAsync(ann))!;
        });
        var right = CountingAuthenticator.Code(key, Start.ToUnixTimeSeconds() / 30);
        var valid = Enumerable.Range(-2, 5).Select(offset => CountingAuthenticator.Code(key, (Start.ToUnixTimeSeconds() / 30) + offset)).ToHashSet();
        List<string> wrong = [.. Enumerable.Range(100_000, 100).Select(code => code.ToString(System.Globalization.CultureInfo.InvariantCulture)).Where(code => !valid.Contains(code)).Take(30)];
        List<string> codes = [.. wrong[..15], right, .. wrong[15..]];
        var gate = new Gate(codes.Count);

        var answers = await Task.WhenAll(codes.Select(code => fixture.WithAsync(async services =>
        {
            var ann = (await services.GetRequiredService<CmsUserManager>().FindByIdAsync(user.Id.ToString("D")))!;
            await gate.PassAsync();
            try
            {
                await services.GetRequiredService<TwoFactorService>().RegenerateRecoveryCodesAsync(ann, code);
                return "";
            }
            catch (FluentCMS.Content.ValidationException refused)
            {
                return refused.Errors.ShouldHaveSingleItem().Code;
            }
        }))).WaitAsync(TimeSpan.FromMinutes(2), Token);

        var checks = counting.Checked.ToList();
        checks.ShouldNotBeEmpty(string.Join(", ", answers));
        checks.TakeWhile(check => !check.Right).Count().ShouldBeLessThanOrEqualTo(limit, string.Join(", ", checks));
        answers.Count(answer => answer == "").ShouldBe(checks.Count(check => check.Right));
        answers.Where(answer => answer != "").ShouldAllBe(answer => answer == IdentityErrorCodes.InvalidTwoFactorCode || answer == IdentityErrorCodes.InvalidUser, string.Join(", ", answers));
        if (!checks.Any(check => check.Right))
        {
            checks.Count.ShouldBeLessThanOrEqualTo(limit);
        }
    }

    // The save of new recovery codes, made once a right code was taken, loses to another save of the user. It is made again on
    // what is stored, the code is not checked again, and the codes answered are those stored, which all redeem (#3666).
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RecoveryCodesWhoseSaveLostAreMadeAgainAndTheStoredOnesAnswered(bool regenerate)
    {
        using var fixture = CodesFixture(out var between, out var counting);
        var (user, code, _) = await TwoFactorUserAsync(fixture, on: regenerate);
        between.Arm(1, () => fixture.Read(context => context.Set<CmsUser>().Where(row => row.Id == user.Id)
            .ExecuteUpdate(setters => setters.SetProperty(row => row.ConcurrencyStamp, Guid.NewGuid().ToString()))));

        var codes = await fixture.WithAsync(async services =>
        {
            var twoFactor = services.GetRequiredService<TwoFactorService>();
            var ann = (await services.GetRequiredService<CmsUserManager>().FindByIdAsync(user.Id.ToString("D")))!;
            return regenerate ? await twoFactor.RegenerateRecoveryCodesAsync(ann, code) : await twoFactor.VerifyAsync(ann, code);
        });

        (between.Disarm(), counting.Checked.Count).ShouldBe((1, 1));
        codes.Count.ShouldBe(TwoFactorService.RecoveryCodeCount);
        await fixture.UsersAsync(async users =>
        {
            var ann = (await users.FindByIdAsync(user.Id.ToString("D")))!;
            ann.TwoFactorEnabled.ShouldBeTrue();
            (await users.CountRecoveryCodesAsync(ann)).ShouldBe(codes.Count);
            foreach (var each in codes)
            {
                Succeeded(await users.RedeemTwoFactorRecoveryCodeAsync(ann, each));
            }

            (await users.CountRecoveryCodesAsync(ann)).ShouldBe(0);
            return true;
        });
    }

    // When every try of that save loses, the request is refused with the code of a save that keeps losing, after the tries of
    // the manager's other saves, with its code checked once, and the codes it was to replace stay (#3666).
    [Fact]
    public async Task RecoveryCodesWhoseSavesKeepLosingAreRefusedAndTheOldOnesKept()
    {
        using var fixture = CodesFixture(out var between, out var counting);
        var (user, code, old) = await TwoFactorUserAsync(fixture, on: true);
        between.Arm(int.MaxValue, () => fixture.Read(context => context.Set<CmsUser>().Where(row => row.Id == user.Id)
            .ExecuteUpdate(setters => setters.SetProperty(row => row.ConcurrencyStamp, Guid.NewGuid().ToString()))));

        var refused = await Should.ThrowAsync<FluentCMS.Content.ValidationException>(() => fixture.WithAsync(async services =>
            await services.GetRequiredService<TwoFactorService>().RegenerateRecoveryCodesAsync((await services.GetRequiredService<CmsUserManager>().FindByIdAsync(user.Id.ToString("D")))!, code)));

        refused.Errors.ShouldHaveSingleItem().Code.ShouldBe(IdentityErrorCodes.InvalidUser);
        (between.Disarm(), counting.Checked.Count).ShouldBe((2 * new AdminUserOptions().Lockout.MaxFailedAccessAttempts, 1));
        await fixture.UsersAsync(async users =>
        {
            var ann = (await users.FindByIdAsync(user.Id.ToString("D")))!;
            (await users.CountRecoveryCodesAsync(ann)).ShouldBe(old.Count);
            Succeeded(await users.RedeemTwoFactorRecoveryCodeAsync(ann, old[0]));
            return true;
        });
    }

    // A try made again runs on the user as stored: one whose two-factor the other save turned off gets no codes and is told
    // two-factor is off, and one the other save deactivated gets none and is refused as a save that lost, without a try more.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecoveryCodesWhoseSaveLostToTurningOffOrDeactivatingAreNotStored(bool deactivate)
    {
        using var fixture = CodesFixture(out var between, out _);
        var (user, code, old) = await TwoFactorUserAsync(fixture, on: true);
        List<string> twoFactorTokens = ["AuthenticatorKey", "RecoveryCodes", OneTimeAuthenticatorTokenProvider.LastStepName];
        between.Arm(1, () => fixture.Read(context =>
        {
            if (!deactivate)
            {
                context.Set<IdentityUserToken<Guid>>().Where(row => row.UserId == user.Id && row.LoginProvider == "[AspNetUserStore]" && twoFactorTokens.Contains(row.Name)).ExecuteDelete();
            }

            return context.Set<CmsUser>().Where(row => row.Id == user.Id).ExecuteUpdate(setters => setters
                .SetProperty(row => row.TwoFactorEnabled, deactivate)
                .SetProperty(row => row.DeactivatedAt, deactivate ? Start : (DateTimeOffset?)null)
                .SetProperty(row => row.ConcurrencyStamp, Guid.NewGuid().ToString()));
        }));

        var refused = await Should.ThrowAsync<FluentCMS.Content.ValidationException>(() => fixture.WithAsync(async services =>
            await services.GetRequiredService<TwoFactorService>().RegenerateRecoveryCodesAsync((await services.GetRequiredService<CmsUserManager>().FindByIdAsync(user.Id.ToString("D")))!, code)));

        refused.Errors.ShouldHaveSingleItem().Code.ShouldBe(deactivate ? IdentityErrorCodes.InvalidUser : IdentityErrorCodes.TwoFactorNotEnabled);
        between.Disarm().ShouldBe(1);
        var stored = await fixture.FindAsync(user.Id);
        (await fixture.UsersAsync(users => users.CountRecoveryCodesAsync(stored))).ShouldBe(deactivate ? old.Count : 0);
    }

    // A failed save that reloaded the user with what another request stored puts nothing the call changed back over it, so a
    // later save of the scope keeps the other request's flag, deactivation or reactivation. No caller of core saves again
    // after such a failure, so this was latent (#3447).
    [Fact]
    public async Task AFailedSaveKeepsTheFlagAndTheDeactivationAnotherRequestStored()
    {
        var user = await _fixture.CreateUserAsync("ann");
        async Task<CmsUser> AfterAnotherSaveAsync(bool flag, DateTimeOffset? deactivated, Action<UpdateSettersBuilder<CmsUser>> other, Func<CmsUserManager, CmsUser, string, Task<IdentityResult>> call)
        {
            _fixture.Read(context => context.Set<CmsUser>().Where(row => row.Id == user.Id)
                .ExecuteUpdate(setters => setters.SetProperty(row => row.MustChangePassword, flag).SetProperty(row => row.DeactivatedAt, deactivated)));
            await _fixture.UsersAsync(async users =>
            {
                var ann = (await users.FindByIdAsync(user.Id.ToString("D")))!;
                var reset = await users.GeneratePasswordResetTokenAsync(ann);
                _fixture.Read(context => context.Set<CmsUser>().Where(row => row.Id == user.Id).ExecuteUpdate(setters =>
                {
                    other(setters);
                    setters.SetProperty(row => row.ConcurrencyStamp, Guid.NewGuid().ToString());
                }));

                (await call(users, ann, reset)).Errors.ShouldHaveSingleItem().Code.ShouldBe(nameof(IdentityErrorDescriber.ConcurrencyFailure));
                Succeeded(await users.UpdateAsync(ann));
                return true;
            });

            return await _fixture.FindAsync(user.Id);
        }

        var set = await AfterAnotherSaveAsync(false, null, setters => setters.SetProperty(row => row.MustChangePassword, true), (users, ann, _) => users.SetPasswordAsync(ann, "Temporary-horse-3"));
        var reset = await AfterAnotherSaveAsync(true, null, setters => setters.SetProperty(row => row.MustChangePassword, false), (users, ann, token) => users.ResetPasswordAsync(ann, token, "Another-horse-2"));
        var required = await AfterAnotherSaveAsync(false, null, setters => setters.SetProperty(row => row.MustChangePassword, true), (users, ann, _) => users.RequirePasswordChangeAsync(ann));
        var deactivated = await AfterAnotherSaveAsync(false, null, setters => setters.SetProperty(row => row.DeactivatedAt, Start), (users, ann, _) => users.DeactivateAsync(ann));
        var reactivated = await AfterAnotherSaveAsync(false, Start, setters => setters.SetProperty(row => row.DeactivatedAt, (DateTimeOffset?)null), (users, ann, _) => users.ReactivateAsync(ann));

        (set.MustChangePassword, reset.MustChangePassword, required.MustChangePassword, deactivated.DeactivatedAt, reactivated.DeactivatedAt).ShouldBe((true, false, true, (DateTimeOffset?)Start, (DateTimeOffset?)null));
    }

    // Identity's hasher, which records each check of a password: the hash it was checked against, and the guess.
    private sealed class CountingHasher : IPasswordHasher<CmsUser>
    {
        private readonly PasswordHasher<CmsUser> _inner = new();
        private int _hashed;

        private Action? _afterNextCheck;

        public System.Collections.Concurrent.ConcurrentQueue<(string Hash, string Provided)> Verified { get; } = new();

        public int Hashed => Volatile.Read(ref _hashed);

        public void Clear()
        {
            Verified.Clear();
            Interlocked.Exchange(ref _hashed, 0);
        }

        public string HashPassword(CmsUser user, string password)
        {
            Interlocked.Increment(ref _hashed);
            return _inner.HashPassword(user, password);
        }

        // Another request's save, made once just after the next check, as it would come between the check and the save.
        public void AfterNextCheck(Action other) => Volatile.Write(ref _afterNextCheck, other);

        public PasswordVerificationResult VerifyHashedPassword(CmsUser user, string hashedPassword, string providedPassword)
        {
            Verified.Enqueue((hashedPassword, providedPassword));
            var result = _inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
            Interlocked.Exchange(ref _afterNextCheck, null)?.Invoke();
            return result;
        }
    }

    // The authenticator's provider of core, which records each code it checks, in the order the checks end, and whether it
    // was taken.
    private sealed class CountingAuthenticator : IUserTwoFactorTokenProvider<CmsUser>
    {
        public IUserTwoFactorTokenProvider<CmsUser>? Inner { get; set; }

        public TimeProvider Clock { get; set; } = TimeProvider.System;

        public System.Collections.Concurrent.ConcurrentQueue<(string Code, bool Right)> Checked { get; } = new();

        private IUserTwoFactorTokenProvider<CmsUser> Provider => Inner ?? new OneTimeAuthenticatorTokenProvider(Clock);

        public Task<string> GenerateAsync(string purpose, UserManager<CmsUser> manager, CmsUser user) => Provider.GenerateAsync(purpose, manager, user);

        public Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<CmsUser> manager, CmsUser user) => Provider.CanGenerateTwoFactorTokenAsync(manager, user);

        public async Task<bool> ValidateAsync(string purpose, string token, UserManager<CmsUser> manager, CmsUser user)
        {
            var right = await Provider.ValidateAsync(purpose, token, manager, user);
            Checked.Enqueue((token, right));
            return right;
        }

        // RFC 6238 with SHA-1, 30 second steps and six digits, as authenticator apps and core compute it.
        public static string Code(string key, long step)
        {
            const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            List<byte> secret = [];
            int buffer = 0, bits = 0;
            foreach (var character in key.TrimEnd('=').ToUpperInvariant())
            {
                buffer = ((buffer << 5) | Alphabet.IndexOf(character, StringComparison.Ordinal)) & 0xffff;
                bits += 5;
                if (bits >= 8)
                {
                    secret.Add((byte)(buffer >> (bits - 8)));
                    bits -= 8;
                }
            }

            var counter = new byte[8];
            System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, step);
#pragma warning disable CA5350 // RFC 6238 uses HMAC-SHA1.
            var hash = System.Security.Cryptography.HMACSHA1.HashData(secret.ToArray(), counter);
#pragma warning restore CA5350
            var offset = hash[^1] & 0x0f;
            var value = (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset)) & 0x7fffffff) % 1_000_000;
            return value.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    // A fixture whose authenticator counts the codes it checks, and in which another save of a user can be made just before
    // a save of its recovery codes.
    private static IdentityFixture CodesFixture(out SaveBeforeCodes between, out CountingAuthenticator counting)
    {
        var before = between = new SaveBeforeCodes();
        var authenticator = counting = new CountingAuthenticator();
        var fixture = new IdentityFixture(cms =>
        {
            cms.Services.AddScoped<IUserValidator<CmsUser>>(services => new SaveBeforeCodes.Validator(services.GetRequiredService<SystemDbContext>(), before));
            cms.Services.PostConfigure<IdentityOptions>(options =>
            {
                authenticator.Inner = options.Tokens.ProviderMap[options.Tokens.AuthenticatorTokenProvider].ProviderInstance as IUserTwoFactorTokenProvider<CmsUser>;
                options.Tokens.ProviderMap[options.Tokens.AuthenticatorTokenProvider] = new TokenProviderDescriptor(typeof(CountingAuthenticator)) { ProviderInstance = authenticator };
            });
        });
        counting.Clock = fixture.Clock;
        return fixture;
    }

    // A user with a key, the right code of it now, and, with two-factor on, the recovery codes it holds.
    private static Task<(CmsUser User, string Code, IReadOnlyList<string> Codes)> TwoFactorUserAsync(IdentityFixture fixture, bool on) =>
        fixture.UsersAsync(async users =>
        {
            var ann = new CmsUser { UserName = "ann", Email = "ann@example.com" };
            Succeeded(await users.CreateAsync(ann, Password));
            Succeeded(await users.SetPendingAuthenticatorKeyAsync(ann));
            IReadOnlyList<string> codes = [];
            if (on)
            {
                Succeeded(await users.SetTwoFactorEnabledAsync(ann, true));
                codes = [.. (await users.GenerateNewTwoFactorRecoveryCodesAsync(ann, TwoFactorService.RecoveryCodeCount))!];
            }

            return (ann, CountingAuthenticator.Code((await users.GetAuthenticatorKeyAsync(ann))!, Start.ToUnixTimeSeconds() / 30), codes);
        });

    // While armed, another request's save of the user, made through a connection of its own, comes just before each save that
    // stores the user's recovery codes, as many times as it is armed for, so that the save loses to it. It runs in a user
    // validator, which Identity calls before the save's transaction opens.
    private sealed class SaveBeforeCodes
    {
        private Action? _other;
        private int _left;
        private int _made;

        public void Arm(int times, Action other) => (_other, _left, _made) = (other, times, 0);

        public int Disarm()
        {
            (_other, _left) = (null, 0);
            return _made;
        }

        public sealed class Validator(SystemDbContext context, SaveBeforeCodes before) : IUserValidator<CmsUser>
        {
            public Task<IdentityResult> ValidateAsync(UserManager<CmsUser> manager, CmsUser user)
            {
                if (before._other is { } other && before._left > 0 && context.ChangeTracker.Entries<IdentityUserToken<Guid>>()
                    .Any(entry => entry.Entity.UserId == user.Id && entry.Entity.Name == "RecoveryCodes" && entry.State is EntityState.Added or EntityState.Modified))
                {
                    before._left--;
                    before._made++;
                    other();
                }

                return Task.FromResult(IdentityResult.Success);
            }
        }
    }

    // Lets the requests go on only once each has read the user, so that all of them read it as it was before any saved.
    private sealed class Gate(int count)
    {
        private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public int Count => count;

        public Task PassAsync()
        {
            if (Interlocked.Increment(ref _arrived) == count)
            {
                _open.TrySetResult();
            }

            return _open.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
        }
    }

    // While armed, every save of a user loses, as to another request's save that went through just before it, and is counted.
    private sealed class LosingSaves : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        private int _armed;
        private int _passing;
        private int _saves;

        // The first saves, as many as passing, go through.
        public void Arm(int passing = 0)
        {
            Volatile.Write(ref _passing, passing);
            Volatile.Write(ref _armed, 1);
        }

        public int Disarm()
        {
            Volatile.Write(ref _armed, 0);
            return Interlocked.Exchange(ref _saves, 0);
        }

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _armed) == 1)
            {
                foreach (var entry in eventData.Context!.ChangeTracker.Entries<CmsUser>().Where(entry => entry.State == EntityState.Modified))
                {
                    if (Interlocked.Increment(ref _saves) > Volatile.Read(ref _passing))
                    {
                        entry.Property(user => user.ConcurrencyStamp).OriginalValue = "another-request";
                    }
                }
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    // Two saves at once, one that names a user by an address and one that gives another user that address, each pass the
    // check a validator makes before the save. They run one after the other under the lock of the names, and the second
    // checks again inside its transaction, so it is refused whatever the provider's isolation (#3373).
    [Fact]
    public async Task TwoSavesAtOnceCannotLeaveANameThatIsAnotherUsersAddress()
    {
        var race = new NameRace();
        using var fixture = new IdentityFixture(cms =>
        {
            // The second save waits for the lock, which the fixture's clock would never let it poll.
            cms.Services.AddSingleton(TimeProvider.System);
            cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(race.Saves, race.Locks));
        });
        var ann = await fixture.CreateUserAsync("ann");
        var bob = await fixture.CreateUserAsync("bob");

        race.Arm();
        var named = fixture.UsersAsync(async users => await users.SetUserNameAsync((await users.FindByIdAsync(ann.Id.ToString("D")))!, "carol@example.com"));
        await race.FirstSaveHeld.WaitAsync(TimeSpan.FromSeconds(20), Token);
        var addressed = fixture.UsersAsync(async users => await users.SetEmailAsync((await users.FindByIdAsync(bob.Id.ToString("D")))!, "Carol@Example.com"));

        Succeeded(await named);
        (await addressed).Errors.ShouldHaveSingleItem().Code.ShouldBe(nameof(IdentityErrorDescriber.DuplicateEmail));
        race.SecondWaited.ShouldBeTrue();
        (await fixture.FindAsync(ann.Id)).UserName.ShouldBe("carol@example.com");
        (await fixture.FindAsync(bob.Id)).Email.ShouldBe("bob@example.com");
    }

    // Holds the first save of a user until another context asks for the lock of the names, which is after its validators
    // passed on what was stored before that save.
    private sealed class NameRace
    {
        private readonly TaskCompletionSource _held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _armed;
        private int _saves;

        public NameRace()
        {
            Saves = new SaveHolder(this);
            Locks = new LockWatcher(this);
        }

        public Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor Saves { get; }

        public Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor Locks { get; }

        public Task FirstSaveHeld => _held.Task;

        public bool SecondWaited => _waiting.Task.IsCompletedSuccessfully;

        public void Arm() => Volatile.Write(ref _armed, 1);

        private void Asked(System.Data.Common.DbCommand command)
        {
            if (_held.Task.IsCompleted && command.Parameters.Cast<System.Data.Common.DbParameter>().Any(parameter => Equals(parameter.Value, IdentityChanges.NamesLock)))
            {
                _waiting.TrySetResult();
            }
        }

        private sealed class SaveHolder(NameRace race) : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
        {
            public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
                Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
                Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
                CancellationToken cancellationToken = default)
            {
                if (Volatile.Read(ref race._armed) == 1
                    && eventData.Context!.ChangeTracker.Entries<CmsUser>().Any(entry => entry.State == EntityState.Modified)
                    && Interlocked.Increment(ref race._saves) == 1)
                {
                    race._held.TrySetResult();
                    await race._waiting.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
                }

                return await base.SavingChangesAsync(eventData, result, cancellationToken);
            }
        }

        private sealed class LockWatcher(NameRace race) : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
        {
            public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
                System.Data.Common.DbCommand command,
                Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
                Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result,
                CancellationToken cancellationToken = default)
            {
                race.Asked(command);
                return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
            }
        }
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

    // An authorization of the user with an access token under it, as a sign-in through a client makes.
    private static Task<string> SessionAsync(IdentityFixture fixture, CmsUser user) =>
        fixture.WithAsync(async services =>
        {
            var authorizations = services.GetRequiredService<IOpenIddictAuthorizationManager>();
            var authorization = await authorizations.CreateAsync(
                new OpenIddictAuthorizationDescriptor { Subject = user.Id.ToString("D"), Status = Statuses.Valid, Type = AuthorizationTypes.AdHoc, CreationDate = Start },
                Token);
            var id = (await authorizations.GetIdAsync(authorization, Token))!;
            await services.GetRequiredService<IOpenIddictTokenManager>().CreateAsync(
                new OpenIddictTokenDescriptor { AuthorizationId = id, Subject = user.Id.ToString("D"), Status = Statuses.Valid, Type = TokenTypeHints.AccessToken, CreationDate = Start, ExpirationDate = Start.AddHours(1) },
                Token);
            return id;
        });

    // The status of the authorization, then of its token.
    private static Task<string?[]> StatusesAsync(IdentityFixture fixture, string authorizationId) =>
        fixture.WithAsync(async services =>
        {
            var authorizations = services.GetRequiredService<IOpenIddictAuthorizationManager>();
            var tokens = services.GetRequiredService<IOpenIddictTokenManager>();
            var authorization = (await authorizations.FindByIdAsync(authorizationId, Token))!;
            string? token = null;
            await foreach (var found in tokens.FindByAuthorizationIdAsync(authorizationId, Token))
            {
                token = await tokens.GetStatusAsync(found, Token);
            }

            return new[] { await authorizations.GetStatusAsync(authorization, Token), token };
        });
}
