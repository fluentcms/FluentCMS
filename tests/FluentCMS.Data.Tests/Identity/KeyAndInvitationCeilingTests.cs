using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Identity.ApiKeys;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Core.Storage;
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
    private readonly IdentityFixture _fixture = new();

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

        SetClientRoles(client, BuiltInRoles.AuthorName);

        (await AcceptAsync(token)).Errors.ShouldHaveSingleItem().Code.ShouldBe(IdentityErrorCodes.InvalidInvitation);

        SetClientRoles(client, BuiltInRoles.SuperAdminName);
        IdentityFixture.Succeeded(await AcceptAsync(token));
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
}
