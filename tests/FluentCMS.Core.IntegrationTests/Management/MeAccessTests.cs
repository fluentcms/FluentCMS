using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Sites;
using FluentCMS.Testing;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Management;

// GET /me/access over HTTP on SQLite (#3096): what the caller may do in the site of the request, as the content service
// decides it, for a user, an API key held to its binding and a caller without the management scope.
public sealed class MeAccessTests(AdminFixture fixture) : IClassFixture<AdminFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // An editor that may read people but not publish them, and change a page's title but not its section: the answer says
    // so, and the content service refuses what it says is refused and allows what it says is allowed.
    [Fact]
    public async Task AnEditorReadsWhatTheContentServiceAllowsIt()
    {
        var role = await fixture.CreateRoleAsync($$"""
            { "name": "Editor {{Guid.NewGuid():N}}", "grants": [
              { "permission": "schema.read" },
              { "permission": "activity.read", "constraints": { "sites": ["{{DefaultSite.Id:D}}"] } },
              { "permission": "content.page.read" },
              { "permission": "content.page.create" },
              { "permission": "content.page.update", "constraints": { "fields": ["title"] } },
              { "permission": "content.page.publish" },
              { "permission": "content.person.read", "constraints": { "fields": ["name"] } },
              { "permission": "content.person.update", "constraints": { "rowRule": "own-entries" } }
            ] }
            """);
        var user = await fixture.Host.CreateUserAsync(await RoleNameAsync(role), cancellationToken: Token);
        using var editor = await fixture.Host.AsUserAsync(user);

        var access = await fixture.GetAsync("me/access", editor);

        access.Status.ShouldBe(HttpStatusCode.OK, access.Body?.ToJsonString());
        access.Json["site"]!.GetValue<string>().ShouldBe(DefaultSite.Id.ToString("D"));
        access.Json["locale"]!.GetValue<string>().ShouldBe(DefaultSite.Locale);
        Strings(access.Json["permissions"]).ShouldBe(["activity.read", "schema.read"], ignoreOrder: true);
        Strings(access.Json["instancePermissions"]).ShouldBe(["schema.read"]);
        access.Json["types"]!.AsObject().Select(type => type.Key).ShouldBe(["page", "person"]);

        var page = access.Json["types"]!["page"]!;
        page.ToJsonString().ShouldBe("""{"drafts":true,"create":true,"update":true,"delete":false,"publish":true,"unpublish":false,"creatableFields":["title","section","owner","meta"],"updatableFields":["title"],"conditional":[]}""");
        var person = access.Json["types"]!["person"]!;
        person["publish"]!.GetValue<bool>().ShouldBeFalse();
        person["update"]!.GetValue<bool>().ShouldBeTrue();
        Strings(person["updatableFields"]).ShouldBe(["name"]);
        Strings(person["conditional"]).ShouldBe(["update"]);

        var created = await AdminFixture.SendAsync(editor, HttpMethod.Post, "content/page", """{ "title": "Mine", "section": "news" }""");
        created.Status.ShouldBe(HttpStatusCode.Created, created.Body?.ToJsonString());
        var id = created.Json["id"]!.GetValue<string>();
        (await AdminFixture.SendAsync(editor, HttpMethod.Patch, $"content/page/{id}", """{ "title": "Renamed" }""", "\"1\"")).Status.ShouldBe(HttpStatusCode.OK);
        var section = await AdminFixture.SendAsync(editor, HttpMethod.Patch, $"content/page/{id}", """{ "section": "sport" }""", "\"2\"");
        section.Status.ShouldBe(HttpStatusCode.UnprocessableEntity, section.Body?.ToJsonString());
        section.Body!.ToJsonString().ShouldContain(AuthorizationErrorCodes.FieldNotWritable);
        (await AdminFixture.SendAsync(editor, HttpMethod.Post, $"content/page/{id}/publish", ifMatch: "\"2\"")).Status.ShouldBe(HttpStatusCode.OK);
        (await AdminFixture.SendAsync(editor, HttpMethod.Delete, $"content/page/{id}")).Status.ShouldBe(HttpStatusCode.Forbidden);

        var someone = await fixture.PostAsync("content/person", """{ "name": "Someone" }""");
        someone.Status.ShouldBe(HttpStatusCode.Created, someone.Body?.ToJsonString());
        var personId = someone.Json["id"]!.GetValue<string>();
        (await AdminFixture.SendAsync(editor, HttpMethod.Post, $"content/person/{personId}/publish", ifMatch: "\"1\"")).Status.ShouldBe(HttpStatusCode.Forbidden);
        // The row rule refuses an entry the editor did not create, which is what "conditional" warned of.
        (await AdminFixture.SendAsync(editor, HttpMethod.Patch, $"content/person/{personId}", """{ "name": "Other" }""", "\"1\"")).Status.ShouldBe(HttpStatusCode.NotFound);
    }

    // Every field of its own pages and the title of any page: the update is conditional, since the service lets the other
    // fields through on the editor's own entries only.
    [Fact]
    public async Task AFieldThatOnlyARowRuleGivesMakesTheUpdateConditional()
    {
        var role = await fixture.CreateRoleAsync($$"""
            { "name": "Pages {{Guid.NewGuid():N}}", "grants": [
              { "permission": "content.page.read" },
              { "permission": "content.page.create" },
              { "permission": "content.page.update", "constraints": { "rowRule": "own-entries" } },
              { "permission": "content.page.update", "constraints": { "fields": ["title"] } }
            ] }
            """);
        using var editor = await fixture.Host.AsUserAsync(await fixture.Host.CreateUserAsync(await RoleNameAsync(role), cancellationToken: Token));

        var page = (await fixture.GetAsync("me/access", editor)).Json["types"]!["page"]!;

        page["update"]!.GetValue<bool>().ShouldBeTrue();
        Strings(page["updatableFields"]).ShouldBe(["title", "section", "meta"]);
        Strings(page["conditional"]).ShouldBe(["update"]);

        var others = await fixture.PostAsync("content/page", """{ "title": "Theirs", "section": "news" }""");
        others.Status.ShouldBe(HttpStatusCode.Created, others.Body?.ToJsonString());
        var theirs = others.Json["id"]!.GetValue<string>();
        (await AdminFixture.SendAsync(editor, HttpMethod.Patch, $"content/page/{theirs}", """{ "title": "Renamed" }""", "\"1\"")).Status.ShouldBe(HttpStatusCode.OK);
        var refused = await AdminFixture.SendAsync(editor, HttpMethod.Patch, $"content/page/{theirs}", """{ "section": "sport" }""", "\"2\"");
        refused.Status.ShouldBe(HttpStatusCode.UnprocessableEntity, refused.Body?.ToJsonString());
        refused.Body!.ToJsonString().ShouldContain(AuthorizationErrorCodes.FieldNotWritable);

        var created = await AdminFixture.SendAsync(editor, HttpMethod.Post, "content/page", """{ "title": "Mine", "section": "news" }""");
        created.Status.ShouldBe(HttpStatusCode.Created, created.Body?.ToJsonString());
        var mine = created.Json["id"]!.GetValue<string>();
        (await AdminFixture.SendAsync(editor, HttpMethod.Patch, $"content/page/{mine}", """{ "section": "sport" }""", "\"1\"")).Status.ShouldBe(HttpStatusCode.OK);
    }

    // A core route asks for a grant with no resource limit, so one limited to resources is not listed as held, and the
    // route refuses as the answer says.
    [Fact]
    public async Task APermissionLimitedToResourcesIsListedAsLimitedNotAsHeld()
    {
        var role = await fixture.CreateRoleAsync($$"""
            { "name": "Limited {{Guid.NewGuid():N}}", "grants": [
              { "permission": "schema.read" },
              { "permission": "users.manage", "constraints": { "resources": ["someone"] } }
            ] }
            """);
        using var limited = await fixture.Host.AsUserAsync(await fixture.Host.CreateUserAsync(await RoleNameAsync(role), cancellationToken: Token));

        var access = await fixture.GetAsync("me/access", limited);

        access.Status.ShouldBe(HttpStatusCode.OK, access.Body?.ToJsonString());
        Strings(access.Json["permissions"]).ShouldBe(["schema.read"]);
        Strings(access.Json["instancePermissions"]).ShouldBe(["schema.read"]);
        Strings(access.Json["limitedPermissions"]).ShouldBe(["users.manage"]);
        (await fixture.GetAsync("users", limited)).Status.ShouldBe(HttpStatusCode.Forbidden);
    }

    // A grant shows nothing of a type the caller may not read, not even a write it holds on it.
    [Fact]
    public async Task ATypeTheCallerMayNotReadIsLeftOut()
    {
        using var writer = await fixture.ClientWithAsync("content.page.read", "content.person.update", "content.person.create");

        var access = await fixture.GetAsync("me/access", writer);

        access.Status.ShouldBe(HttpStatusCode.OK, access.Body?.ToJsonString());
        access.Json["types"]!.AsObject().Select(type => type.Key).ShouldBe(["page"]);
        Strings(access.Json["permissions"]).ShouldBeEmpty();
        access.Json.ToJsonString().ShouldNotContain("person");
    }

    // A key is held to the role, the sites and the types it is bound to; a key of the Delivery class, which holds no
    // management scope, reads nothing here.
    [Fact]
    public async Task AKeyIsHeldToItsBindingAndScope()
    {
        var role = await fixture.CreateRoleAsync($$"""{ "name": "Key role {{Guid.NewGuid():N}}", "grants": [ { "permission": "schema.read" }, { "permission": "content.*.read" }, { "permission": "content.*.publish" } ] }""");
        var bound = await fixture.PostAsync("api-keys", $$"""{ "name": "bound-{{Guid.NewGuid():N}}", "class": "management", "roleId": "{{role}}", "siteIds": ["{{DefaultSite.Id:D}}"], "typeNames": ["page"] }""");
        bound.Status.ShouldBe(HttpStatusCode.Created, bound.Body?.ToJsonString());
        using var key = fixture.Host.CreateClient();
        key.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", bound.Json["secret"]!.GetValue<string>());

        var access = await fixture.GetAsync("me/access", key);

        // A key bound to types holds only the content permissions of those types.
        access.Status.ShouldBe(HttpStatusCode.OK, access.Body?.ToJsonString());
        Strings(access.Json["permissions"]).ShouldBeEmpty();
        Strings(access.Json["instancePermissions"]).ShouldBeEmpty();
        access.Json["types"]!.AsObject().Select(type => type.Key).ShouldBe(["page"]);
        access.Json["types"]!["page"]!["publish"]!.GetValue<bool>().ShouldBeTrue();
        access.Json["types"]!["page"]!["update"]!.GetValue<bool>().ShouldBeFalse();

        // One bound to a site holds its permissions there and none for the instance.
        var site = await fixture.PostAsync("api-keys", $$"""{ "name": "site-{{Guid.NewGuid():N}}", "class": "management", "roleId": "{{role}}", "siteIds": ["{{DefaultSite.Id:D}}"] }""");
        site.Status.ShouldBe(HttpStatusCode.Created, site.Body?.ToJsonString());
        using var siteKey = fixture.Host.CreateClient();
        siteKey.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", site.Json["secret"]!.GetValue<string>());
        var ofSite = await fixture.GetAsync("me/access", siteKey);
        Strings(ofSite.Json["permissions"]).ShouldBe(["schema.read"]);
        Strings(ofSite.Json["instancePermissions"]).ShouldBeEmpty();
        ofSite.Json["types"]!.AsObject().Select(type => type.Key).ShouldBe(["page", "person"]);

        var delivery = await fixture.Host.CreateApiKeyAsync(ApiKeyClass.Delivery, BuiltInRoles.SuperAdminName, cancellationToken: Token);
        using var reader = fixture.Host.AsKey(delivery);
        (await fixture.GetAsync("me/access", reader)).Status.ShouldBe(HttpStatusCode.Forbidden);

        using var anonymous = fixture.Host.CreateClient();
        (await fixture.GetAsync("me/access", anonymous)).Status.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ASuperAdminHoldsEveryPermissionAndEveryOperation()
    {
        var access = await fixture.GetAsync("me/access");

        Strings(access.Json["instancePermissions"]).ShouldContain("users.manage");
        Strings(access.Json["instancePermissions"]).ShouldContain("settings.mail.manage");
        Strings(access.Json["permissions"]).ShouldBe(Strings(access.Json["instancePermissions"]));
        Strings(access.Json["permissions"]).ShouldNotContain(name => name.StartsWith("content.", StringComparison.Ordinal));
        access.Json["types"]!["page"]!["conditional"]!.AsArray().ShouldBeEmpty();
        access.Json["types"]!["page"]!["unpublish"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public async Task TheRouteTakesNoQuery() =>
        (await fixture.GetAsync("me/access?unknown=x")).Status.ShouldBe(HttpStatusCode.BadRequest);

    private async Task<string> RoleNameAsync(string id) => (await fixture.GetAsync($"roles/{id}")).Json["name"]!.GetValue<string>();

    private static List<string> Strings(JsonNode? array) => [.. array!.AsArray().Select(item => item!.GetValue<string>())];
}
