using System.Net;
using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Identity.ApiKeys;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Core.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.EntityFrameworkCore.Models;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Identity.IdentityFixture;

namespace FluentCMS.Data.Tests.Identity;

// A narrower operator does not take over, cut off or cancel what is above its roles, and an invitation does not outlive the
// standing of the credential that sent it.
public sealed class KeyAndInvitationCeilingTests : IDisposable
{
    private readonly Window _window = new();
    private readonly IdentityFixture _fixture;

    public KeyAndInvitationCeilingTests() =>
        _fixture = new IdentityFixture(cms => cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(_window)));

    public void Dispose() => _fixture.Dispose();

    private Task<RoleInfo> RoleAsync(string name, params string[] permissions) =>
        _fixture.WithAsync(services => services.GetRequiredService<RoleService>().CreateAsync(
            new RoleRequest(name, null, [.. permissions.Select(permission => new PermissionGrant(permission, PermissionConstraints.None))]), Token));

    private async Task<Principal> OperatorAsync(string name, params string[] permissions)
    {
        var role = await RoleAsync($"{name} role", permissions);
        return PrincipalOf(await _fixture.CreateUserAsync(name, Password, role.Id));
    }

    private Task<CreatedApiKey> CreateKeyAsync(Guid role) =>
        _fixture.WithAsync(services => services.GetRequiredService<ApiKeyService>().CreateAsync(new ApiKeyRequest("Key", ApiKeyClass.Management, role), Token));

    private Task<T> KeysAsync<T>(Principal principal, Func<ApiKeyService, Task<T>> work) =>
        _fixture.Database.AsAsync(principal, services => work(services.GetRequiredService<ApiKeyService>()));

    private Task<T> InvitationsAsync<T>(Principal principal, Func<InvitationService, Task<T>> work) =>
        _fixture.Database.AsAsync(principal, services => work(services.GetRequiredService<InvitationService>()));

    private Task<IdentityResult> AcceptAsync(string token) =>
        _fixture.WithAsync(services => services.GetRequiredService<InvitationService>().AcceptAsync(token, "bob", Password, Token));

    private Task<bool> AcceptedAsync() =>
        _fixture.UsersAsync(async users => await users.FindByEmailAsync("bob@example.com") is not null);

    private Task<string> InviteAsync(Principal inviter, params Guid[] roles) => InviteToAsync(inviter, "bob@example.com", roles);

    private async Task<string> InviteToAsync(Principal inviter, string address, params Guid[] roles)
    {
        await InvitationsAsync(inviter, invitations => invitations.InviteAsync(address, roles, AcceptUrl, Token));
        return TokenOf(_fixture.Email.Sent.Last());
    }

    private Principal KeyPrincipal(Guid id) =>
        ApiKeyClaims.PrincipalOf(_fixture.Read(context => context.Set<ApiKey>().AsNoTracking().Single(key => key.Id == id)));

    [Fact]
    public async Task AKeyAboveTheCallersRolesIsNotRotatedRevokedOrExpired()
    {
        var caller = await OperatorAsync("keys", CorePermissions.ApiKeysManage);
        var above = await CreateKeyAsync(BuiltInRoles.SuperAdmin);
        var within = await CreateKeyAsync(_fixture.Read(context => context.Set<CmsUserRole>().AsNoTracking().Single()).RoleId);

        await Should.ThrowAsync<ManagementAccessDeniedException>(() => KeysAsync(caller, keys => keys.RotateAsync(above.Key.Id, cancellationToken: Token)));
        await Should.ThrowAsync<ManagementAccessDeniedException>(() => KeysAsync(caller, keys => keys.RevokeAsync(above.Key.Id, Token)));
        await Should.ThrowAsync<ManagementAccessDeniedException>(() => KeysAsync(caller, async keys =>
        {
            await keys.ExpireAsync(above.Key.Id, cancellationToken: Token);
            return true;
        }));

        var row = _fixture.Read(context => context.Set<ApiKey>().AsNoTracking().Single(key => key.Id == above.Key.Id));
        row.KeyHash.ShouldBe(ApiKeyFormat.Hash(above.Secret));
        row.RevokedAt.ShouldBeNull();
        row.ExpiresAt.ShouldBeNull();

        (await KeysAsync(caller, keys => keys.RotateAsync(within.Key.Id, cancellationToken: Token))).Secret.ShouldNotBe(within.Secret);
        (await KeysAsync(caller, keys => keys.RevokeAsync(within.Key.Id, Token))).ShouldBeTrue();
    }

    [Fact]
    public async Task AnInvitationAboveTheCallersRolesIsNotRevoked()
    {
        var caller = await OperatorAsync("people", CorePermissions.UsersManage);
        var sent = await _fixture.WithAsync(services => services.GetRequiredService<InvitationService>().InviteAsync("bob@example.com", [BuiltInRoles.SuperAdmin], AcceptUrl, Token));

        await Should.ThrowAsync<ManagementAccessDeniedException>(() => InvitationsAsync(caller, invitations => invitations.RevokeAsync(sent.Id, Token)));
        _fixture.Invitations().Single().RevokedAt.ShouldBeNull();

        var callerRole = _fixture.Read(context => context.Set<CmsUserRole>().AsNoTracking().Single()).RoleId;
        var own = await _fixture.WithAsync(services => services.GetRequiredService<InvitationService>().InviteAsync("carl@example.com", [callerRole], AcceptUrl, Token));
        (await InvitationsAsync(caller, invitations => invitations.RevokeAsync(own.Id, Token))).ShouldBeTrue();
    }

    [Fact]
    public async Task ANewInvitationDoesNotEndOneAboveTheCallersRoles()
    {
        var caller = await OperatorAsync("people", CorePermissions.UsersManage);
        var callerRole = _fixture.Read(context => context.Set<CmsUserRole>().AsNoTracking().Single()).RoleId;
        await _fixture.WithAsync(services => services.GetRequiredService<InvitationService>().InviteAsync("bob@example.com", [BuiltInRoles.SuperAdmin], AcceptUrl, Token));

        await Should.ThrowAsync<ManagementAccessDeniedException>(() => InvitationsAsync(caller, invitations => invitations.InviteAsync("bob@example.com", [], AcceptUrl, Token)));
        await Should.ThrowAsync<ManagementAccessDeniedException>(() => InvitationsAsync(caller, invitations => invitations.InviteAsync("bob@example.com", [callerRole], AcceptUrl, Token)));
        _fixture.Invitations().ShouldHaveSingleItem().RevokedAt.ShouldBeNull();

        // The address is matched as users are, so a change of case does not get around the check.
        await Should.ThrowAsync<ManagementAccessDeniedException>(() => InvitationsAsync(caller, invitations => invitations.InviteAsync("Bob@Example.com", [], AcceptUrl, Token)));
        _fixture.Invitations().ShouldHaveSingleItem().RevokedAt.ShouldBeNull();

        await _fixture.WithAsync(services => services.GetRequiredService<InvitationService>().InviteAsync("carl@example.com", [callerRole], AcceptUrl, Token));
        await InvitationsAsync(caller, invitations => invitations.InviteAsync("carl@example.com", [], AcceptUrl, Token));
        _fixture.Invitations().Single(row => row.Email == "carl@example.com" && row.RoleIds != "[]").RevokedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task AnExpiredInvitationAboveTheCallersRolesDoesNotHoldBackANewOne()
    {
        var caller = await OperatorAsync("people", CorePermissions.UsersManage);
        var callerRole = _fixture.Read(context => context.Set<CmsUserRole>().AsNoTracking().Single()).RoleId;
        await _fixture.WithAsync(services => services.GetRequiredService<InvitationService>().InviteAsync("bob@example.com", [BuiltInRoles.SuperAdmin], AcceptUrl, Token));
        _fixture.Clock.Advance(TimeSpan.FromDays(8));

        var token = await InviteAsync(caller, callerRole);

        var rows = _fixture.Invitations();
        rows.Count.ShouldBe(2);
        rows.ShouldAllBe(row => row.AcceptedAt == null);
        rows.Single(row => row.RoleIds.Contains(BuiltInRoles.SuperAdmin.ToString("D"), StringComparison.OrdinalIgnoreCase)).RevokedAt.ShouldNotBeNull();
        IdentityFixture.Succeeded(await AcceptAsync(token));
        (await _fixture.UsersAsync(async users => await users.GetRoleIdsAsync((await users.FindByEmailAsync("bob@example.com"))!))).ShouldBe([callerRole]);
    }

    [Fact]
    public async Task AnInvitationWhoseInviterNoLongerManagesUsersIsRefusedAtAccept()
    {
        var people = await RoleAsync("People", CorePermissions.UsersManage);
        var readers = await RoleAsync("Readers", "content.article.read");
        var admin = await _fixture.CreateUserAsync("admin", Password, people.Id, readers.Id);
        var token = await InviteAsync(PrincipalOf(admin), readers.Id);

        await _fixture.UsersAsync(async users => (await users.RemoveFromRoleAsync((await users.FindByIdAsync(admin.Id.ToString("D")))!, people.Id)).Succeeded);

        (await AcceptAsync(token)).Errors.ShouldHaveSingleItem().Code.ShouldBe(IdentityErrorCodes.InvalidInvitation);
        (await AcceptedAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task ACallerWithoutThePermissionIsToldNothingOfTheIdsAKeyNames()
    {
        var caller = await OperatorAsync("reader", "content.article.read");
        ApiKeyRequest[] requests =
        [
            new("Real role", ApiKeyClass.Management, BuiltInRoles.Editor),
            new("Made-up role", ApiKeyClass.Management, Guid.CreateVersion7()),
            new("Real site", ApiKeyClass.Management, BuiltInRoles.Editor) { SiteIds = [FluentCMS.Data.Tests.Content.ServiceDatabase.SiteA.Value] },
            new("Made-up site", ApiKeyClass.Management, BuiltInRoles.Editor) { SiteIds = [Guid.CreateVersion7()] },
            new("Real role, no site", ApiKeyClass.Management, BuiltInRoles.Editor) { SiteIds = [] },
            new("Made-up role, no site", ApiKeyClass.Management, Guid.CreateVersion7()) { SiteIds = [] },
        ];

        foreach (var request in requests)
        {
            await Should.ThrowAsync<ManagementAccessDeniedException>(() => KeysAsync(caller, keys => keys.CreateAsync(request, Token)), request.Name);
        }
    }

    [Fact]
    public async Task AnInvitationSentByAKeyEndsWhenTheKeyIsRevoked()
    {
        var key = await CreateKeyAsync(BuiltInRoles.SuperAdmin);
        var token = await InviteAsync(KeyPrincipal(key.Key.Id), BuiltInRoles.Editor);

        await _fixture.WithAsync(services => services.GetRequiredService<ApiKeyService>().RevokeAsync(key.Key.Id, Token));

        _fixture.Invitations().Single().RevokedAt.ShouldNotBeNull();
        (await AcceptAsync(token)).Errors.ShouldHaveSingleItem().Code.ShouldBe(IdentityErrorCodes.InvalidInvitation);
        (await AcceptedAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task AnInvitationSentByAKeyThatExpiredOrWasRevokedWithoutItIsRefusedAtAccept()
    {
        var expiring = await CreateKeyAsync(BuiltInRoles.SuperAdmin);
        var token = await InviteAsync(KeyPrincipal(expiring.Key.Id), BuiltInRoles.Editor);
        var later = await CreateKeyAsync(BuiltInRoles.SuperAdmin);
        var laterToken = await InviteToAsync(KeyPrincipal(later.Key.Id), "carl@example.com", BuiltInRoles.Editor);
        await _fixture.WithAsync(async services =>
        {
            await services.GetRequiredService<ApiKeyService>().ExpireAsync(expiring.Key.Id, cancellationToken: Token);
            await services.GetRequiredService<ApiKeyService>().ExpireAsync(later.Key.Id, _fixture.Clock.GetUtcNow().AddHours(1), Token);
            return true;
        });

        // Ending now ends its invitations; ending later leaves them to the check at accept.
        _fixture.Invitations().Single(row => row.Email == "bob@example.com").RevokedAt.ShouldNotBeNull();
        _fixture.Invitations().Single(row => row.Email == "carl@example.com").RevokedAt.ShouldBeNull();
        _fixture.Clock.Advance(TimeSpan.FromHours(2));
        (await _fixture.WithAsync(services => services.GetRequiredService<InvitationService>().AcceptAsync(laterToken, "carl", Password, Token)))
            .Errors.ShouldHaveSingleItem().Code.ShouldBe(IdentityErrorCodes.InvalidInvitation);

        (await AcceptAsync(token)).Errors.ShouldHaveSingleItem().Code.ShouldBe(IdentityErrorCodes.InvalidInvitation);
        (await AcceptedAsync()).ShouldBeFalse();

        var revoked = await CreateKeyAsync(BuiltInRoles.SuperAdmin);
        token = await InviteAsync(KeyPrincipal(revoked.Key.Id), BuiltInRoles.Editor);
        _fixture.Read(context => context.Set<ApiKey>().Where(key => key.Id == revoked.Key.Id).ExecuteUpdate(setters => setters.SetProperty(key => key.RevokedAt, _fixture.Clock.GetUtcNow())));

        (await AcceptAsync(token)).Errors.ShouldHaveSingleItem().Code.ShouldBe(IdentityErrorCodes.InvalidInvitation);

        var standing = await CreateKeyAsync(BuiltInRoles.SuperAdmin);
        IdentityFixture.Succeeded(await AcceptAsync(await InviteAsync(KeyPrincipal(standing.Key.Id), BuiltInRoles.Editor)));
    }

    [Fact]
    public async Task AnInvitationWhoseInviterNoLongerHoldsItsRolesIsRefusedAtAccept()
    {
        var people = await RoleAsync("People", CorePermissions.UsersManage);
        var readers = await RoleAsync("Readers", "content.article.read");
        var admin = await _fixture.CreateUserAsync("admin", Password, people.Id, readers.Id);
        var token = await InviteAsync(PrincipalOf(admin), readers.Id);
        var other = await InviteToAsync(PrincipalOf(admin), "carl@example.com", readers.Id);

        await _fixture.UsersAsync(async users => (await users.RemoveFromRoleAsync((await users.FindByIdAsync(admin.Id.ToString("D")))!, readers.Id)).Succeeded);

        (await _fixture.WithAsync(services => services.GetRequiredService<InvitationService>().FindAsync(token, Token))).ShouldBeNull();
        (await AcceptAsync(token)).Errors.ShouldHaveSingleItem().Code.ShouldBe(IdentityErrorCodes.InvalidInvitation);
        (await AcceptedAsync()).ShouldBeFalse();
        _fixture.Invitations().ShouldAllBe(row => row.AcceptedAt == null);

        await _fixture.UsersAsync(async users => (await users.AddToRoleAsync((await users.FindByIdAsync(admin.Id.ToString("D")))!, readers.Id)).Succeeded);
        IdentityFixture.Succeeded(await _fixture.WithAsync(services => services.GetRequiredService<InvitationService>().AcceptAsync(other, "carl", Password, Token)));
    }

    [Fact]
    public async Task AnInvitationOfAServiceAccountStandsOnlyWhileItsClientStillGivesTheRoles()
    {
        var client = Guid.CreateVersion7();
        SetClientRoles(client, BuiltInRoles.SuperAdminName, add: true);
        var account = new Principal(PrincipalKind.ServiceAccount, client, "Deploy", new Dictionary<string, string>
        {
            [TokenScopes.Claim] = TokenScopes.Management,
            [CmsClaimTypes.Roles] = BuiltInRoles.SuperAdmin.ToString("D"),
        });
        var token = await InviteAsync(account, BuiltInRoles.Editor);
        await _fixture.WithAsync(services => services.GetRequiredService<InvitationService>().InviteAsync("carl@example.com", [BuiltInRoles.SuperAdmin], AcceptUrl, Token));
        (await ListedAsync()).ShouldBe(["bob@example.com", "carl@example.com"], ignoreOrder: true);

        SetClientRoles(client, BuiltInRoles.AuthorName);

        // The system's own invitation is not checked again, so it stays listed (#3391).
        (await ListedAsync()).ShouldBe(["carl@example.com"]);
        (await AcceptAsync(token)).Errors.ShouldHaveSingleItem().Code.ShouldBe(IdentityErrorCodes.InvalidInvitation);

        SetClientRoles(client, BuiltInRoles.SuperAdminName);
        (await ListedAsync()).ShouldBe(["bob@example.com", "carl@example.com"], ignoreOrder: true);
        IdentityFixture.Succeeded(await AcceptAsync(token));
    }

    private async Task<List<string>> ListedAsync() =>
        [.. (await _fixture.WithAsync(services => services.GetRequiredService<InvitationService>().ListPendingAsync(Token))).Select(invitation => invitation.Email)];

    // The list leaves out what accept would refuse for its sender, and writes nothing to it (#3391).
    [Fact]
    public async Task ThePendingListLeavesOutInvitationsWhoseSenderNoLongerStands()
    {
        var people = await RoleAsync("People", CorePermissions.UsersManage);
        var readers = await RoleAsync("Readers", "content.article.read");
        var writers = await RoleAsync("Writers", "content.article.update");
        var demoted = await _fixture.CreateUserAsync("demoted", Password, people.Id, readers.Id);
        var standing = await _fixture.CreateUserAsync("standing", Password, people.Id, readers.Id, writers.Id);
        var key = await CreateKeyAsync(BuiltInRoles.SuperAdmin);

        var bob = await InviteAsync(PrincipalOf(demoted), readers.Id);
        var carl = await InviteToAsync(KeyPrincipal(key.Key.Id), "carl@example.com", BuiltInRoles.Editor);
        await InviteToAsync(PrincipalOf(standing), "dave@example.com", readers.Id);
        await InviteToAsync(PrincipalOf(standing), "erin@example.com", readers.Id);
        var fay = await InviteToAsync(PrincipalOf(standing), "fay@example.com", writers.Id);

        await _fixture.UsersAsync(async users => (await users.RemoveFromRoleAsync((await users.FindByIdAsync(demoted.Id.ToString("D")))!, people.Id)).Succeeded);
        await _fixture.UsersAsync(async users => (await users.RemoveFromRoleAsync((await users.FindByIdAsync(standing.Id.ToString("D")))!, writers.Id)).Succeeded);
        await _fixture.WithAsync(async services =>
        {
            await services.GetRequiredService<ApiKeyService>().ExpireAsync(key.Key.Id, _fixture.Clock.GetUtcNow().AddHours(1), Token);
            return true;
        });
        _fixture.Clock.Advance(TimeSpan.FromHours(2));

        var listed = await _fixture.WithAsync(services => services.GetRequiredService<InvitationService>().ListPendingAsync(Token));

        listed.Select(invitation => invitation.Email).ShouldBe(["dave@example.com", "erin@example.com"], ignoreOrder: true);
        _fixture.Invitations().ShouldAllBe(row => row.RevokedAt == null && row.AcceptedAt == null);
        (await AcceptAsync(bob)).Errors.ShouldHaveSingleItem().Code.ShouldBe(IdentityErrorCodes.InvalidInvitation);
        (await _fixture.WithAsync(services => services.GetRequiredService<InvitationService>().AcceptAsync(carl, "carl", Password, Token)))
            .Errors.ShouldHaveSingleItem().Code.ShouldBe(IdentityErrorCodes.InvalidInvitation);
        (await _fixture.WithAsync(services => services.GetRequiredService<InvitationService>().AcceptAsync(fay, "fay", Password, Token)))
            .Errors.ShouldHaveSingleItem().Code.ShouldBe(IdentityErrorCodes.InvalidInvitation);
    }

    // apikeys.manage and the ceiling of a key change are checked again in its transaction, with the caller's grants read
    // there: a caller who loses either after the checks before it is refused (#3391).
    [Fact]
    public async Task AKeyChangeIsRefusedWhenTheCallersGrantsAreCutBeforeItsTransaction()
    {
        var keys = await RoleAsync("Keys", CorePermissions.ApiKeysManage);
        var readers = await RoleAsync("Readers", "content.article.read");
        var user = await _fixture.CreateUserAsync("keys", Password, keys.Id, readers.Id);
        var caller = PrincipalOf(user);
        var key = await CreateKeyAsync(readers.Id);
        Func<ApiKeyService, Task>[] changes =
        [
            service => service.CreateAsync(new ApiKeyRequest("Another", ApiKeyClass.Management, readers.Id), Token),
            service => service.RotateAsync(key.Key.Id, cancellationToken: Token),
            service => service.RevokeAsync(key.Key.Id, Token),
            service => service.ExpireAsync(key.Key.Id, cancellationToken: Token),
            service => service.SetAllowedAddressesAsync(key.Key.Id, ["10.0.0.0/8"], Token),
        ];

        foreach (var cut in new[] { readers.Id, keys.Id })
        {
            foreach (var change in changes)
            {
                _window.Arm(() => _fixture.UsersAsync(async users => (await users.RemoveFromRoleAsync((await users.FindByIdAsync(user.Id.ToString("D")))!, cut)).Succeeded));

                await Should.ThrowAsync<ManagementAccessDeniedException>(() => KeysAsync(caller, async service =>
                {
                    await change(service);
                    return true;
                }));

                _window.Armed.ShouldBeFalse();
                await _fixture.UsersAsync(async users => (await users.AddToRoleAsync((await users.FindByIdAsync(user.Id.ToString("D")))!, cut)).Succeeded);
            }
        }

        var row = _fixture.Read(context => context.Set<ApiKey>().AsNoTracking().Single(stored => stored.Id == key.Key.Id));
        row.KeyHash.ShouldBe(ApiKeyFormat.Hash(key.Secret));
        row.RevokedAt.ShouldBeNull();
        row.ExpiresAt.ShouldBeNull();
        row.AllowedAddresses.ShouldBeNull();
        _fixture.Read(context => context.Set<ApiKey>().Count()).ShouldBe(1);
    }

    // A request holds the row of the key it authenticated; a change it makes reads that key again in its transaction, so a
    // calling key revoked by another request in between is refused (#3391).
    [Fact]
    public async Task AKeyChangeIsRefusedWhenTheCallingKeyIsRevokedBeforeItsTransaction()
    {
        var manager = await RoleAsync("Key manager", CorePermissions.ApiKeysManage, "content.article.read");
        var readers = await RoleAsync("Readers", "content.article.read");
        var calling = await CreateKeyAsync(manager.Id);
        var key = await CreateKeyAsync(readers.Id);
        var verified = _fixture.Read(context => context.Set<ApiKey>().AsNoTracking().Single(stored => stored.Id == calling.Key.Id));

        Task<CreatedApiKey> RotateAsVerifiedAsync() => _fixture.Database.AsAsync(ApiKeyClaims.PrincipalOf(verified), services =>
        {
            var http = new DefaultHttpContext { RequestServices = services };
            ApiKeyAuthentication.Verify(http, verified, calling.Secret);
            services.GetRequiredService<IHttpContextAccessor>().HttpContext = http;
            return services.GetRequiredService<ApiKeyService>().RotateAsync(key.Key.Id, cancellationToken: Token);
        });

        _window.Arm(() => Task.FromResult(_fixture.Read(context => context.Set<ApiKey>()
            .Where(stored => stored.Id == calling.Key.Id)
            .ExecuteUpdate(setters => setters.SetProperty(stored => stored.RevokedAt, _fixture.Clock.GetUtcNow())))));

        await Should.ThrowAsync<ManagementAccessDeniedException>(RotateAsVerifiedAsync);

        _window.Armed.ShouldBeFalse();
        _fixture.Read(context => context.Set<ApiKey>().AsNoTracking().Single(stored => stored.Id == key.Key.Id)).KeyHash.ShouldBe(ApiKeyFormat.Hash(key.Secret));
    }

    // Sending an invitation over a pending one and revoking one read the caller's grants again in their transaction: a
    // caller who loses users.manage or a grant of the roles after the checks before it is refused, and nothing is written
    // or mailed (#3481).
    [Fact]
    public async Task AnInvitationIsNeitherSupersededNorRevokedWhenTheCallersGrantsAreCutBeforeItsTransaction()
    {
        var people = await RoleAsync("People", CorePermissions.UsersManage);
        var readers = await RoleAsync("Readers", "content.article.read");
        var user = await _fixture.CreateUserAsync("people", Password, people.Id, readers.Id);
        var caller = PrincipalOf(user);
        var pending = await _fixture.WithAsync(services => services.GetRequiredService<InvitationService>().InviteAsync("bob@example.com", [readers.Id], AcceptUrl, Token));
        var mailed = _fixture.Email.Sent.Count;
        Func<InvitationService, Task>[] writes =
        [
            service => service.InviteAsync("bob@example.com", [], AcceptUrl, Token),
            service => service.RevokeAsync(pending.Id, Token),
        ];

        foreach (var cut in new[] { readers.Id, people.Id })
        {
            foreach (var write in writes)
            {
                _window.Arm(() => _fixture.UsersAsync(async users => (await users.RemoveFromRoleAsync((await users.FindByIdAsync(user.Id.ToString("D")))!, cut)).Succeeded));

                await Should.ThrowAsync<ManagementAccessDeniedException>(() => InvitationsAsync(caller, async service =>
                {
                    await write(service);
                    return true;
                }));

                _window.Armed.ShouldBeFalse();
                await _fixture.UsersAsync(async users => (await users.AddToRoleAsync((await users.FindByIdAsync(user.Id.ToString("D")))!, cut)).Succeeded);
            }
        }

        _fixture.Invitations().ShouldHaveSingleItem().RevokedAt.ShouldBeNull();
        _fixture.Email.Sent.Count.ShouldBe(mailed);
        (await InvitationsAsync(caller, service => service.RevokeAsync(pending.Id, Token))).ShouldBeTrue();
    }

    // A change made by a key reads the key again in its transaction and refuses it as the authentication handler would now:
    // a secret that a rotation without overlap retired, the replaced secret once its overlap ended, or a client its addresses
    // no longer allow. A rotation with an overlap leaves the request's secret good (#3483).
    [Fact]
    public async Task AChangeByAKeyIsRefusedWhenItsSecretOrAddressNoLongerAuthenticatesTheRequestBeforeItsTransaction()
    {
        var manager = await RoleAsync("Key manager", CorePermissions.ApiKeysManage, CorePermissions.UsersManage, "content.article.read");
        var readers = await RoleAsync("Readers", "content.article.read");
        var calling = await CreateKeyAsync(manager.Id);
        var key = await CreateKeyAsync(readers.Id);
        var pending = await _fixture.WithAsync(services => services.GetRequiredService<InvitationService>().InviteAsync("bob@example.com", [readers.Id], AcceptUrl, Token));
        var client = IPAddress.Parse("203.0.113.9");

        Task<ApiKeyInfo> NarrowAsync(string secret) =>
            AsCallingKeyAsync(calling.Key.Id, secret, client, services => services.GetRequiredService<ApiKeyService>().SetAllowedAddressesAsync(key.Key.Id, ["10.0.0.0/8"], Token));
        Task<CreatedApiKey> RotateCallingAsync(TimeSpan? overlap) =>
            _fixture.WithAsync(services => services.GetRequiredService<ApiKeyService>().RotateAsync(calling.Key.Id, overlap, Token));
        Task SetCallingAddressesAsync(params string[] addresses) =>
            _fixture.WithAsync(services => services.GetRequiredService<ApiKeyService>().SetAllowedAddressesAsync(calling.Key.Id, addresses, Token));

        // A rotation without overlap retires the secret the request came with, for a key change and for an invitation.
        var secret = calling.Secret;
        _window.Arm(async () => secret = (await RotateCallingAsync(TimeSpan.Zero)).Secret);
        await Should.ThrowAsync<ManagementAccessDeniedException>(() => NarrowAsync(calling.Secret));
        _window.Armed.ShouldBeFalse();

        var presented = secret;
        _window.Arm(async () => secret = (await RotateCallingAsync(TimeSpan.Zero)).Secret);
        await Should.ThrowAsync<ManagementAccessDeniedException>(() => AsCallingKeyAsync(calling.Key.Id, presented, client, services => services.GetRequiredService<InvitationService>().RevokeAsync(pending.Id, Token)));
        _window.Armed.ShouldBeFalse();

        // One with an overlap keeps it good until the overlap ends.
        var current = secret;
        _window.Arm(async () => secret = (await RotateCallingAsync(null)).Secret);
        (await NarrowAsync(current)).AllowedAddresses.ShouldBe(["10.0.0.0/8"]);
        await SetAddressesAsync(key.Key.Id, null);

        _window.Arm(() =>
        {
            _fixture.Clock.Advance(TimeSpan.FromDays(2));
            return Task.CompletedTask;
        });
        await Should.ThrowAsync<ManagementAccessDeniedException>(() => NarrowAsync(current));
        _window.Armed.ShouldBeFalse();

        // Addresses narrowed past the client refuse the change; ones that still allow it do not.
        _window.Arm(() => SetCallingAddressesAsync("10.0.0.0/8"));
        await Should.ThrowAsync<ManagementAccessDeniedException>(() => NarrowAsync(secret));
        _window.Armed.ShouldBeFalse();

        await SetCallingAddressesAsync("10.0.0.0/8", "203.0.113.0/24");
        _window.Arm(() => SetCallingAddressesAsync("203.0.113.0/24"));
        (await NarrowAsync(secret)).AllowedAddresses.ShouldBe(["10.0.0.0/8"]);

        _fixture.Invitations().ShouldHaveSingleItem().RevokedAt.ShouldBeNull();
    }

    private Task<ApiKeyInfo> SetAddressesAsync(Guid id, IReadOnlyCollection<string>? addresses) =>
        _fixture.WithAsync(services => services.GetRequiredService<ApiKeyService>().SetAllowedAddressesAsync(id, addresses, Token));

    // As a request whose key the authentication handler verified with this secret from this client.
    private Task<T> AsCallingKeyAsync<T>(Guid id, string secret, IPAddress client, Func<IServiceProvider, Task<T>> work)
    {
        var verified = _fixture.Read(context => context.Set<ApiKey>().AsNoTracking().Single(stored => stored.Id == id));
        return _fixture.Database.AsAsync(ApiKeyClaims.PrincipalOf(verified), services =>
        {
            var http = new DefaultHttpContext { RequestServices = services };
            http.Connection.RemoteIpAddress = client;
            ApiKeyAuthentication.Verify(http, verified, secret);
            services.GetRequiredService<IHttpContextAccessor>().HttpContext = http;
            return work(services);
        });
    }

    private void SetClientRoles(Guid client, string role, bool add = false) =>
        _fixture.Read(context =>
        {
            var properties = JsonSerializer.Serialize(new Dictionary<string, string[]> { [AuthorizationServer.RolesProperty] = [role] });
            if (add)
            {
                context.Add(new OpenIddictEntityFrameworkCoreApplication<Guid> { Id = client, ClientId = "deploy", ClientType = "confidential", Properties = properties });
                return context.SaveChanges();
            }

            return context.Set<OpenIddictEntityFrameworkCoreApplication<Guid>>().Where(row => row.Id == client)
                .ExecuteUpdate(setters => setters.SetProperty(row => row.Properties, properties));
        });

    // Runs code once as the next transaction of the system tables is about to open, which is after the checks a change makes
    // before its transaction, as another request that commits then would.
    private sealed class Window : Microsoft.EntityFrameworkCore.Diagnostics.DbTransactionInterceptor
    {
        private Func<Task>? _work;

        public bool Armed => Volatile.Read(ref _work) is not null;

        public void Arm(Func<Task> work) => Volatile.Write(ref _work, work);

        public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbTransaction>> TransactionStartingAsync(
            System.Data.Common.DbConnection connection,
            Microsoft.EntityFrameworkCore.Diagnostics.TransactionStartingEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _work, null) is { } work)
            {
                await work();
            }

            return await base.TransactionStartingAsync(connection, eventData, result, cancellationToken);
        }
    }
}
