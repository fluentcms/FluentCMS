using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Events;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Sites;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Management;

// Users, invitations, roles with grants and API keys over HTTP on SQLite (#2786), and the rule that no caller hands out more
// than it holds.
public sealed class IdentityEndpointsTests(AdminFixture fixture) : IClassFixture<AdminFixture>
{
    [Fact]
    public async Task ARoleWithGrantsIsCreatedReadChangedAndDeleted()
    {
        var created = await fixture.PostAsync("roles", """
            { "name": "Reviewer", "description": "Reads pages", "grants": [
              { "permission": "content.page.read", "constraints": { "locales": ["EN"], "fields": ["title"] } },
              { "permission": "schema.read" } ] }
            """);

        created.Status.ShouldBe(HttpStatusCode.Created, created.Body?.ToJsonString());
        var id = created.Json["id"]!.GetValue<string>();
        created.Headers.Location!.ToString().ShouldBe($"{AdminFixture.Root}roles/{id}");
        created.Json["builtIn"]!.GetValue<bool>().ShouldBeFalse();
        var grant = created.Json["grants"]!.AsArray()[0]!;
        grant["permission"]!.GetValue<string>().ShouldBe("content.page.read");
        grant["constraints"]!["locales"]!.AsArray().Select(locale => locale!.GetValue<string>()).ShouldBe(["en"]);
        created.Json["grants"]!.AsArray()[1]!["constraints"].ShouldBeNull();

        (await fixture.GetAsync($"roles/{id}")).Json["description"]!.GetValue<string>().ShouldBe("Reads pages");

        var updated = await fixture.PutAsync($"roles/{id}", """{ "name": "Reader", "grants": [ { "permission": "content.*.read" } ] }""");
        updated.Status.ShouldBe(HttpStatusCode.OK, updated.Body?.ToJsonString());
        updated.Json["name"]!.GetValue<string>().ShouldBe("Reader");
        updated.Json["description"].ShouldBeNull();
        updated.Json["grants"]!.AsArray().ShouldHaveSingleItem()!["permission"]!.GetValue<string>().ShouldBe("content.*.read");

        var listed = (await fixture.GetAsync("roles")).Json["items"]!.AsArray().Select(item => item!["name"]!.GetValue<string>()).ToList();
        listed.ShouldContain("Reader");
        listed.ShouldContain(BuiltInRoles.SuperAdminName);

        (await fixture.DeleteAsync($"roles/{id}")).Status.ShouldBe(HttpStatusCode.NoContent);
        var gone = await fixture.GetAsync($"roles/{id}");
        gone.Status.ShouldBe(HttpStatusCode.NotFound);
        gone.Json["code"]!.GetValue<string>().ShouldBe("FCMS3040");
    }

    [Fact]
    public async Task ARoleIsRefusedForWhatTheCatalogueAndTheRowRulesDoNotHave()
    {
        var refused = await fixture.PostAsync("roles", """
            { "name": "Broken", "grants": [
              { "permission": "nothing.here" },
              { "permission": "content.page.update", "constraints": { "rowRule": "no-such-rule" } },
              { "permission": "content.page.read", "constraints": { "sites": [] } } ] }
            """);

        refused.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        refused.Json["code"]!.GetValue<string>().ShouldBe("FCMS3042");
        refused.Json["errors"]!.AsArray().Select(error => error!["path"]!.GetValue<string>())
            .ShouldBe(["grants[0].permission", "grants[1].constraints.rowRule", "grants[2].constraints.sites"], ignoreOrder: true);

        var unknown = await fixture.PostAsync("roles", """{ "name": "Broken", "grants": [ { "permission": "content.page.read", "constraints": { "colour": "red" } } ] }""");
        unknown.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        unknown.Json["errors"]!.AsArray().ShouldHaveSingleItem()!["path"]!.GetValue<string>().ShouldBe("grants[0].constraints");

        var superAdmin = await fixture.PutAsync($"roles/{BuiltInRoles.SuperAdmin:D}", """{ "name": "Super Admin", "grants": [] }""");
        superAdmin.Status.ShouldBe(HttpStatusCode.Conflict);
        superAdmin.Json["code"]!.GetValue<string>().ShouldBe("FCMS3043");

        var renamed = await fixture.PutAsync($"roles/{BuiltInRoles.Editor:D}", """{ "name": "Writer", "grants": [] }""");
        renamed.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        renamed.Json["code"]!.GetValue<string>().ShouldBe("FCMS3041");

        (await fixture.DeleteAsync($"roles/{BuiltInRoles.Author:D}")).Json["code"]!.GetValue<string>().ShouldBe("FCMS3043");
    }

    [Fact]
    public async Task AnInvitedUserGetsItsRolesAndIsManagedEndToEnd()
    {
        var roleId = await fixture.CreateRoleAsync("""{ "name": "Page editor", "grants": [ { "permission": "content.page.*" } ] }""");
        var email = $"{Guid.NewGuid():N}@example.com";

        var sent = await fixture.PostAsync("invitations", $$"""{ "email": "{{email}}", "roleIds": ["{{roleId}}"] }""");
        sent.Status.ShouldBe(HttpStatusCode.Created, sent.Body?.ToJsonString());
        sent.Json["email"]!.GetValue<string>().ShouldBe(email);
        var pending = (await fixture.GetAsync("invitations")).Json["items"]!.AsArray().Single(item => item!["email"]!.GetValue<string>() == email)!;
        pending["roleIds"]!.AsArray().Select(role => role!.GetValue<string>()).ShouldBe([roleId]);

        var message = fixture.Email.Sent.Last(item => item.To == email);
        message.TextBody.ShouldContain("/account/accept-invitation?token=");
        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            var accepted = await scope.ServiceProvider.GetRequiredService<InvitationService>()
                .AcceptAsync(AdminFixture.TokenOf(message), email, "Correct-horse-battery-1", TestContext.Current.CancellationToken);
            accepted.Succeeded.ShouldBeTrue();
        }

        var found = await fixture.GetAsync($"users?search={Uri.EscapeDataString(email)}");
        found.Json["total"]!.GetValue<long>().ShouldBe(1);
        var user = found.Json["items"]!.AsArray().ShouldHaveSingleItem()!;
        var userId = user["id"]!.GetValue<string>();
        RoleIds(user).ShouldBe([roleId]);

        var changed = await fixture.PatchAsync($"users/{userId}", """{ "mustChangePassword": true }""");
        changed.Json["mustChangePassword"]!.GetValue<bool>().ShouldBeTrue();

        RoleIds((await fixture.DeleteAsync($"users/{userId}/roles/{roleId}")).Json).ShouldBeEmpty();
        RoleIds((await fixture.PutAsync($"users/{userId}/roles/{roleId}")).Json).ShouldBe([roleId]);
        (await fixture.PutAsync($"users/{userId}/roles/{roleId}")).Json["code"]!.GetValue<string>().ShouldBe("FCMS3050");

        (await fixture.PostAsync($"users/{userId}/deactivate")).Json["deactivatedAt"].ShouldNotBeNull();
        (await fixture.PostAsync($"users/{userId}/reactivate")).Json["deactivatedAt"].ShouldBeNull();
        (await fixture.GetAsync("invitations")).Json["items"]!.AsArray().ShouldNotContain(item => item!["email"]!.GetValue<string>() == email);
    }

    [Fact]
    public async Task AnInvitationIsRevoked()
    {
        var sent = await fixture.PostAsync("invitations", $$"""{ "email": "{{Guid.NewGuid():N}}@example.com" }""");
        var id = sent.Json["id"]!.GetValue<string>();

        (await fixture.DeleteAsync($"invitations/{id}")).Status.ShouldBe(HttpStatusCode.NoContent);
        var again = await fixture.DeleteAsync($"invitations/{id}");
        again.Status.ShouldBe(HttpStatusCode.NotFound);
        again.Json["code"]!.GetValue<string>().ShouldBe("FCMS3048");
    }

    [Fact]
    public async Task ARoleIsNotDeletedWhileAUserOrAKeyThatIsNotRevokedHoldsIt()
    {
        var name = $"Holder {Guid.NewGuid():N}";
        var roleId = await fixture.CreateRoleAsync($$"""{ "name": "{{name}}", "grants": [ { "permission": "content.page.read" } ] }""");
        var user = await fixture.Host.CreateUserAsync(name, cancellationToken: TestContext.Current.CancellationToken);
        var key = await fixture.PostAsync("api-keys", $$"""{ "name": "Reader", "class": "delivery", "roleId": "{{roleId}}" }""");
        var keyId = key.Json["id"]!.GetValue<string>();

        var held = await fixture.DeleteAsync($"roles/{roleId}");
        held.Status.ShouldBe(HttpStatusCode.Conflict);
        held.Json["code"]!.GetValue<string>().ShouldBe("FCMS3044");

        (await fixture.DeleteAsync($"users/{user.Id:D}/roles/{roleId}")).Status.ShouldBe(HttpStatusCode.OK);
        (await fixture.DeleteAsync($"roles/{roleId}")).Status.ShouldBe(HttpStatusCode.Conflict);
        (await fixture.DeleteAsync($"api-keys/{keyId}")).Status.ShouldBe(HttpStatusCode.NoContent);
        (await fixture.DeleteAsync($"roles/{roleId}")).Status.ShouldBe(HttpStatusCode.NoContent);

        // The holder lost the role with its event, and the key was revoked with its own, before the role went.
        await DrainOutboxAsync();
        var roleChanged = await fixture.GetAsync($"activity?action=user.role_changed&targetId={user.Id:D}");
        roleChanged.Json["items"]!.AsArray().ShouldContain(item => item!["details"]!["removed"]!.GetValue<string>() == roleId);
        (await fixture.GetAsync($"activity?action=api_key.revoked&targetId={keyId}")).Json["items"]!.AsArray().ShouldHaveSingleItem();
        (await fixture.GetAsync($"activity?action=role.deleted&targetId={roleId}")).Json["items"]!.AsArray().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task AnApiKeyIsCreatedOnceListedUsedAndRevoked()
    {
        var created = await fixture.PostAsync("api-keys", $$"""
            { "name": "Site reader", "class": "delivery", "roleId": "{{BuiltInRoles.Editor:D}}", "siteIds": ["{{DefaultSite.Id:D}}"], "rateLimit": 100 }
            """);

        created.Status.ShouldBe(HttpStatusCode.Created, created.Body?.ToJsonString());
        var id = created.Json["id"]!.GetValue<string>();
        var secret = created.Json["secret"]!.GetValue<string>();
        secret.ShouldStartWith("fcms_dk_");
        created.Json["class"]!.GetValue<string>().ShouldBe("delivery");

        var read = await fixture.GetAsync($"api-keys/{id}");
        read.Json["secret"].ShouldBeNull();
        read.Json["siteIds"]!.AsArray().Select(site => site!.GetValue<string>()).ShouldBe([DefaultSite.Id.ToString("D")]);
        (await fixture.GetAsync("api-keys")).Json["items"]!.AsArray().ShouldContain(item => item!["id"]!.GetValue<string>() == id);

        using var delivery = fixture.Host.CreateClient();
        delivery.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", secret);
        (await delivery.GetAsync(new Uri("/api/delivery/v1/page", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var expired = await fixture.PostAsync($"api-keys/{id}/expire", """{ "at": "2099-01-01T00:00:00Z" }""");
        expired.Json["expiresAt"]!.GetValue<DateTimeOffset>().ShouldBe(new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero));

        (await fixture.DeleteAsync($"api-keys/{id}")).Status.ShouldBe(HttpStatusCode.NoContent);
        (await delivery.GetAsync(new Uri("/api/delivery/v1/page", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await fixture.GetAsync($"api-keys/{id}")).Json["revokedAt"].ShouldNotBeNull();
    }

    [Fact]
    public async Task TheSessionsOfAUserAreListedAndEnded()
    {
        var user = await fixture.Host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: TestContext.Current.CancellationToken);
        using var client = await fixture.Host.AsUserAsync(user);
        (await fixture.GetAsync("me", client)).Status.ShouldBe(HttpStatusCode.OK);

        var sessions = await fixture.GetAsync($"users/{user.Id:D}/sessions");
        var session = sessions.Json["items"]!.AsArray().ShouldHaveSingleItem()!;
        session["clientId"]!.GetValue<string>().ShouldBe(FluentCmsTestHost.OAuthClientId);
        var sessionId = session["id"]!.GetValue<string>();
        (await fixture.GetAsync("me/sessions", client)).Json["items"]!.AsArray().ShouldHaveSingleItem()!["id"]!.GetValue<string>().ShouldBe(sessionId);

        (await fixture.DeleteAsync($"users/{user.Id:D}/sessions/{sessionId}")).Status.ShouldBe(HttpStatusCode.NoContent);
        (await fixture.GetAsync("me", client)).Status.ShouldBe(HttpStatusCode.Unauthorized);
        (await fixture.DeleteAsync($"users/{user.Id:D}/sessions/{sessionId}")).Json["code"]!.GetValue<string>().ShouldBe("FCMS3047");
    }

    [Fact]
    public async Task AUserEndsItsOwnSession()
    {
        var user = await fixture.Host.CreateUserAsync(BuiltInRoles.AuthorName, cancellationToken: TestContext.Current.CancellationToken);
        using var client = await fixture.Host.AsUserAsync(user);
        var sessionId = (await fixture.GetAsync("me/sessions", client)).Json["items"]!.AsArray().ShouldHaveSingleItem()!["id"]!.GetValue<string>();

        (await fixture.DeleteAsync($"me/sessions/{sessionId}", client)).Status.ShouldBe(HttpStatusCode.NoContent);
        (await fixture.GetAsync("me", client)).Status.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ACallerHandsOutNoRoleThatGrantsMoreThanItHolds()
    {
        using var manager = await fixture.ClientWithAsync(CorePermissions.UsersManage, CorePermissions.RolesManage, "content.page.read");

        (await fixture.PostAsync("roles", """{ "name": "Everything", "grants": [ { "permission": "*" } ] }""", manager)).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.PostAsync("roles", """{ "name": "Pages, all fields", "grants": [ { "permission": "content.page.update" } ] }""", manager)).Status.ShouldBe(HttpStatusCode.Forbidden);
        var narrower = await fixture.CreateRoleAsync($$"""{ "name": "Page reader {{Guid.NewGuid():N}}", "grants": [ { "permission": "content.page.read", "constraints": { "fields": ["title"] } } ] }""", manager);

        var email = $"{Guid.NewGuid():N}@example.com";
        (await fixture.PostAsync("invitations", $$"""{ "email": "{{email}}", "roleIds": ["{{BuiltInRoles.SuperAdmin:D}}"] }""", manager)).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.PostAsync("invitations", $$"""{ "email": "{{email}}", "roleIds": ["{{narrower}}"] }""", manager)).Status.ShouldBe(HttpStatusCode.Created);

        var author = await fixture.Host.CreateUserAsync(BuiltInRoles.AuthorName, cancellationToken: TestContext.Current.CancellationToken);
        (await fixture.PutAsync($"users/{author.Id:D}/roles/{BuiltInRoles.SuperAdmin:D}", client: manager)).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.PostAsync($"users/{author.Id:D}/deactivate", client: manager)).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.PatchAsync($"users/{author.Id:D}", """{ "email": "taken-over@example.com" }""", manager)).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.PutAsync($"roles/{BuiltInRoles.Editor:D}", """{ "name": "Editor", "grants": [] }""", manager)).Status.ShouldBe(HttpStatusCode.Forbidden);

        var reader = await fixture.Host.CreateUserAsync(BuiltInRoles.PublicName, cancellationToken: TestContext.Current.CancellationToken);
        (await fixture.PutAsync($"users/{reader.Id:D}/roles/{narrower}", client: manager)).Status.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AKeyBoundToASiteNeverMintsAWiderKey()
    {
        var roleName = await fixture.RoleWithAsync(CorePermissions.ApiKeysManage, "content.page.read");
        var roleId = (await fixture.GetAsync("roles")).Json["items"]!.AsArray().Single(item => item!["name"]!.GetValue<string>() == roleName)!["id"]!.GetValue<string>();
        var bound = await fixture.PostAsync("api-keys", $$"""{ "name": "Site admin key", "class": "management", "roleId": "{{roleId}}", "siteIds": ["{{DefaultSite.Id:D}}"] }""");
        var unbound = await fixture.PostAsync("api-keys", $$"""{ "name": "Instance key", "class": "delivery", "roleId": "{{roleId}}" }""");
        using var key = fixture.Host.CreateClient();
        key.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", bound.Json["secret"]!.GetValue<string>());

        var wider = await fixture.PostAsync("api-keys", $$"""{ "name": "Wider", "class": "delivery", "roleId": "{{roleId}}" }""", key);
        wider.Status.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.PostAsync("api-keys", $$"""{ "name": "More", "class": "delivery", "roleId": "{{BuiltInRoles.Editor:D}}", "siteIds": ["{{DefaultSite.Id:D}}"] }""", key))
            .Status.ShouldBe(HttpStatusCode.Forbidden);
        var within = await fixture.PostAsync("api-keys", $$"""{ "name": "Same", "class": "delivery", "roleId": "{{roleId}}", "siteIds": ["{{DefaultSite.Id:D}}"], "typeNames": ["page"] }""", key);
        within.Status.ShouldBe(HttpStatusCode.Created, within.Body?.ToJsonString());

        var listed = (await fixture.GetAsync("api-keys", key)).Json["items"]!.AsArray().Select(item => item!["id"]!.GetValue<string>()).ToList();
        listed.ShouldContain(within.Json["id"]!.GetValue<string>());
        listed.ShouldNotContain(unbound.Json["id"]!.GetValue<string>());
        (await fixture.DeleteAsync($"api-keys/{unbound.Json["id"]!.GetValue<string>()}", key)).Status.ShouldBe(HttpStatusCode.NotFound);
    }

    private static List<string> RoleIds(JsonNode user) => [.. user["roles"]!.AsArray().Select(role => role!["id"]!.GetValue<string>())];

    private async Task DrainOutboxAsync()
    {
        while (true)
        {
            await using var scope = fixture.Host.Services.CreateAsyncScope();
            if (!await scope.ServiceProvider.GetRequiredService<JobExecutor>().RunNextAsync(OutboxDrainJob.Queue, "test", TestContext.Current.CancellationToken))
            {
                return;
            }
        }
    }
}
