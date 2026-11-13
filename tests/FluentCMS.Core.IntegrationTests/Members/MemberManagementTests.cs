using System.Net;
using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity.Members;
using FluentCMS.Core.IntegrationTests.Identity;
using FluentCMS.Testing;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Members;

// /members and /member-groups of the Management API: who may call them, what a group hands out, and the events and
// activity rows of each change.
public sealed class MemberManagementTests(MemberFixture fixture) : IClassFixture<MemberFixture>
{
    private const string Password = MemberFixture.Password;

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheMemberRoutesNeedMembersManageAndUsersManageIsNotEnough()
    {
        var member = await fixture.Host.CreateMemberAsync(cancellationToken: Token);
        using var others = await fixture.AdminWithAsync(CorePermissions.UsersManage, CorePermissions.RolesManage, "content.*");
        using var manager = await fixture.AdminWithAsync(CorePermissions.MembersManage);

        (HttpMethod Method, string Path, string? Body)[] calls =
        [
            (HttpMethod.Get, "members", null),
            (HttpMethod.Post, "members", $$"""{ "email": "{{MemberFixture.NewAddress()}}" }"""),
            (HttpMethod.Get, $"members/{member.Id:D}", null),
            (HttpMethod.Patch, $"members/{member.Id:D}", """{ "displayName": "Renamed" }"""),
            (HttpMethod.Post, $"members/{member.Id:D}/deactivate", null),
            (HttpMethod.Post, $"members/{member.Id:D}/reactivate", null),
            (HttpMethod.Get, $"members/{member.Id:D}/sessions", null),
            (HttpMethod.Delete, $"members/{member.Id:D}/sessions/{Guid.NewGuid():D}", null),
            (HttpMethod.Put, $"members/{member.Id:D}/groups/{fixture.Gold:D}", null),
            (HttpMethod.Delete, $"members/{member.Id:D}/groups/{fixture.Gold:D}", null),
            (HttpMethod.Delete, $"members/{member.Id:D}", null),
            (HttpMethod.Get, "member-groups", null),
            (HttpMethod.Post, "member-groups", $$"""{ "name": "refused", "roleId": "{{BuiltInRoles.Member:D}}" }"""),
            (HttpMethod.Get, $"member-groups/{fixture.Gold:D}", null),
            (HttpMethod.Put, $"member-groups/{fixture.Gold:D}", $$"""{ "name": "gold", "roleId": "{{BuiltInRoles.Member:D}}" }"""),
            (HttpMethod.Delete, $"member-groups/{fixture.Gold:D}", null),
        ];

        foreach (var (method, path, body) in calls)
        {
            var refused = await fixture.ManageAsync(method, path, body, others);
            refused.Status.ShouldBe(HttpStatusCode.Forbidden, $"{method} {path}");
            refused.Json["code"]!.GetValue<string>().ShouldBe("FCMS3103");

            using var visitor = fixture.Host.CreateSecureClient();
            (await fixture.ManageAsync(method, path, body, visitor)).Status.ShouldBe(HttpStatusCode.Unauthorized, $"{method} {path}");
        }

        // With members.manage alone the member is read, changed and deactivated, and the roles a group may be bound to are listed.
        (await fixture.ManageAsync(HttpMethod.Get, $"members/{member.Id:D}", client: manager)).Status.ShouldBe(HttpStatusCode.OK);
        (await fixture.ManageAsync(HttpMethod.Get, "members", client: manager)).Status.ShouldBe(HttpStatusCode.OK);
        (await fixture.ManageAsync(HttpMethod.Patch, $"members/{member.Id:D}", """{ "displayName": "Renamed" }""", manager)).Json["displayName"]!.GetValue<string>().ShouldBe("Renamed");
        (await fixture.ManageAsync(HttpMethod.Get, "member-groups", client: manager)).Status.ShouldBe(HttpStatusCode.OK);
        (await fixture.ManageAsync(HttpMethod.Get, "roles", client: manager)).Status.ShouldBe(HttpStatusCode.OK);
        (await fixture.ManageAsync(HttpMethod.Get, "users", client: manager)).Status.ShouldBe(HttpStatusCode.Forbidden);

        // A key of the Delivery class is no manager whatever its role grants.
        using var key = fixture.Host.AsKey(await fixture.Host.CreateApiKeyAsync(ApiKeyClass.Delivery, BuiltInRoles.SuperAdminName, cancellationToken: Token));
        (await fixture.ManageAsync(HttpMethod.Get, "members", client: key)).Status.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AGroupIsCreatedReadChangedAndDeletedAndEachChangeIsAnActivityRow()
    {
        var name = $"team-{Guid.NewGuid():N}"[..20];
        var created = await fixture.ManageAsync(HttpMethod.Post, "member-groups", $$"""{ "name": "{{name}}", "description": "A team", "roleId": "{{fixture.InsiderRoleId:D}}" }""");
        created.Status.ShouldBe(HttpStatusCode.Created, created.Body?.ToJsonString());
        var id = created.Json["id"]!.GetValue<string>();
        created.Headers.Location!.ToString().ShouldBe($"{MemberFixture.Management}member-groups/{id}");
        created.Json["roleName"]!.GetValue<string>().ShouldBe(MemberFixture.InsiderRole);
        created.Json["members"]!.GetValue<int>().ShouldBe(0);

        var member = await fixture.Host.CreateMemberAsync(cancellationToken: Token);
        (await fixture.ManageAsync(HttpMethod.Put, $"members/{member.Id:D}/groups/{id}")).Json["groups"]!.AsArray().ShouldHaveSingleItem()!["name"]!.GetValue<string>().ShouldBe(name);
        (await fixture.ManageAsync(HttpMethod.Put, $"members/{member.Id:D}/groups/{id}")).Json["code"]!.GetValue<string>().ShouldBe("FCMS3171");
        (await fixture.ManageAsync(HttpMethod.Get, $"member-groups/{id}")).Json["members"]!.GetValue<int>().ShouldBe(1);
        (await fixture.ManageAsync(HttpMethod.Get, "member-groups")).Json["items"]!.AsArray().Select(item => item!["name"]!.GetValue<string>()).ShouldContain(name);

        var renamed = name + "-b";
        var updated = await fixture.ManageAsync(HttpMethod.Put, $"member-groups/{id}", $$"""{ "name": "{{renamed}}", "roleId": "{{BuiltInRoles.Member:D}}" }""");
        updated.Status.ShouldBe(HttpStatusCode.OK, updated.Body?.ToJsonString());
        updated.Json["name"]!.GetValue<string>().ShouldBe(renamed);
        updated.Json["description"].ShouldBeNull();
        updated.Json["roleId"]!.GetValue<string>().ShouldBe(BuiltInRoles.Member.ToString("D"));

        // What a name and a body may be.
        (await fixture.ManageAsync(HttpMethod.Post, "member-groups", $$"""{ "name": "{{renamed}}", "roleId": "{{BuiltInRoles.Member:D}}" }""")).Json["code"]!.GetValue<string>().ShouldBe("FCMS3170");
        foreach (var bad in (string[])["Upper", "two words", "", "-starts-with-a-dash", new string('a', 65)])
        {
            var refused = await fixture.ManageAsync(HttpMethod.Post, "member-groups", $$"""{ "name": "{{bad}}", "roleId": "{{BuiltInRoles.Member:D}}" }""");
            refused.Status.ShouldBe(HttpStatusCode.UnprocessableEntity, bad);
        }

        (await fixture.ManageAsync(HttpMethod.Post, "member-groups", """{ "name": "no-role" }""")).Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await fixture.ManageAsync(HttpMethod.Post, "member-groups", $$"""{ "name": "no-such-role", "roleId": "{{Guid.NewGuid():D}}" }""")).Json["code"]!.GetValue<string>().ShouldBe("FCMS3169");

        (await fixture.ManageAsync(HttpMethod.Delete, $"members/{member.Id:D}/groups/{id}")).Json["groups"]!.AsArray().ShouldBeEmpty();
        (await fixture.ManageAsync(HttpMethod.Delete, $"members/{member.Id:D}/groups/{id}")).Json["code"]!.GetValue<string>().ShouldBe("FCMS3172");
        (await fixture.ManageAsync(HttpMethod.Delete, $"member-groups/{id}")).Status.ShouldBe(HttpStatusCode.NoContent);
        (await fixture.ManageAsync(HttpMethod.Get, $"member-groups/{id}")).Json["code"]!.GetValue<string>().ShouldBe("FCMS3168");
        (await fixture.ManageAsync(HttpMethod.Put, $"members/{member.Id:D}/groups/{id}")).Json["code"]!.GetValue<string>().ShouldBe("FCMS3168");

        var rows = await fixture.ActivityAsync("member_group.", id);
        rows.Select(row => row.Action).ShouldBe(["member_group.created", "member_group.updated", "member_group.deleted"], ignoreOrder: true);
        rows.ShouldAllBe(row => row.ActorType == PrincipalKind.AdminUser && row.SiteId == null && row.TargetType == "member_group");
        Details(rows.Single(row => row.Action == "member_group.created").Details)["role"].ShouldBe(fixture.InsiderRoleId.ToString("D"));

        var changes = await fixture.ActivityAsync("member.group_changed", member.Id.ToString("D"));
        changes.Count.ShouldBe(2);
        changes.ShouldContain(row => Details(row.Details)["added"] == id && Details(row.Details)["removed"] == "");
        changes.ShouldContain(row => Details(row.Details)["removed"] == id && Details(row.Details)["added"] == "");
    }

    [Fact]
    public async Task BindingARoleToAGroupAndPuttingAMemberInOrOutOfItNeedEveryGrantOfThatRole()
    {
        using var manager = await fixture.AdminWithAsync(CorePermissions.MembersManage, "content.bulletin.read");
        var readerRole = $"bulletin-reader-{Guid.NewGuid():N}";
        var readerRoleId = await fixture.Host.CreateRoleAsync(readerRole, ["content.bulletin.read"], Token);
        var member = await fixture.Host.CreateMemberAsync(cancellationToken: Token);
        var name = $"readers-{Guid.NewGuid():N}"[..20];

        // A role the caller does not hold in full is not bound, whether on create or on a change.
        (await fixture.ManageAsync(HttpMethod.Post, "member-groups", $$"""{ "name": "{{name}}", "roleId": "{{fixture.InsiderRoleId:D}}" }""", manager)).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.ManageAsync(HttpMethod.Post, "member-groups", $$"""{ "name": "{{name}}", "roleId": "{{BuiltInRoles.SuperAdmin:D}}" }""", manager)).Status.ShouldBe(HttpStatusCode.Forbidden);

        var created = await fixture.ManageAsync(HttpMethod.Post, "member-groups", $$"""{ "name": "{{name}}", "roleId": "{{readerRoleId:D}}" }""", manager);
        created.Status.ShouldBe(HttpStatusCode.Created, created.Body?.ToJsonString());
        var id = created.Json["id"]!.GetValue<string>();

        (await fixture.ManageAsync(HttpMethod.Put, $"member-groups/{id}", $$"""{ "name": "{{name}}", "roleId": "{{BuiltInRoles.SuperAdmin:D}}" }""", manager)).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.ManageAsync(HttpMethod.Get, $"member-groups/{id}", client: manager)).Json["roleId"]!.GetValue<string>().ShouldBe(readerRoleId.ToString("D"));

        // A group whose role the caller does not hold is neither renamed nor given a member by it.
        (await fixture.ManageAsync(HttpMethod.Put, $"member-groups/{fixture.Insiders:D}", $$"""{ "name": "insiders-renamed", "roleId": "{{readerRoleId:D}}" }""", manager)).Status.ShouldBe(HttpStatusCode.Forbidden);
        var notGiven = await fixture.ManageAsync(HttpMethod.Put, $"members/{member.Id:D}/groups/{fixture.Insiders:D}", client: manager);
        notGiven.Status.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.ManageAsync(HttpMethod.Put, $"members/{member.Id:D}/groups/{id}", client: manager)).Status.ShouldBe(HttpStatusCode.OK);

        // Nor is a member taken out of it, or the group deleted, since a row rule that negates the group would then let the
        // member read more. Both are refused as putting a member in is.
        (await fixture.ManageAsync(HttpMethod.Put, $"members/{member.Id:D}/groups/{fixture.Insiders:D}")).Status.ShouldBe(HttpStatusCode.OK);
        var notTaken = await fixture.ManageAsync(HttpMethod.Delete, $"members/{member.Id:D}/groups/{fixture.Insiders:D}", client: manager);
        notTaken.Status.ShouldBe(HttpStatusCode.Forbidden);
        notTaken.Json["code"]!.GetValue<string>().ShouldBe(notGiven.Json["code"]!.GetValue<string>());
        var notDeleted = await fixture.ManageAsync(HttpMethod.Delete, $"member-groups/{fixture.Insiders:D}", client: manager);
        notDeleted.Status.ShouldBe(HttpStatusCode.Forbidden);
        notDeleted.Json["code"]!.GetValue<string>().ShouldBe(notGiven.Json["code"]!.GetValue<string>());
        (await fixture.ManageAsync(HttpMethod.Get, $"members/{member.Id:D}", client: manager)).Json["groups"]!.AsArray()
            .Select(group => group!["name"]!.GetValue<string>()).ShouldContain("insiders");

        using var client = await fixture.Host.AsMemberAsync(member);
        (await MemberFixture.TitlesAsync(client, "dossier")).Status.ShouldBe(HttpStatusCode.OK);
        (await fixture.ManageAsync(HttpMethod.Delete, $"members/{member.Id:D}/groups/{fixture.Insiders:D}")).Status.ShouldBe(HttpStatusCode.OK);
        (await MemberFixture.TitlesAsync(client, "dossier")).Status.ShouldBe(HttpStatusCode.NotFound);

        // A group whose role the caller holds is emptied and deleted by it.
        (await fixture.ManageAsync(HttpMethod.Delete, $"members/{member.Id:D}/groups/{id}", client: manager)).Json["groups"]!.AsArray().ShouldBeEmpty();
        (await fixture.ManageAsync(HttpMethod.Delete, $"member-groups/{id}", client: manager)).Status.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task ARoleIsNotDeletedWhileAGroupUsesIt()
    {
        var roleName = $"bound-{Guid.NewGuid():N}";
        var roleId = await fixture.Host.CreateRoleAsync(roleName, ["content.bulletin.read"], Token);
        var created = await fixture.ManageAsync(HttpMethod.Post, "member-groups", $$"""{ "name": "{{$"bound-{Guid.NewGuid():N}"[..20]}}", "roleId": "{{roleId:D}}" }""");
        created.Status.ShouldBe(HttpStatusCode.Created, created.Body?.ToJsonString());
        var groupId = created.Json["id"]!.GetValue<string>();

        var refused = await fixture.ManageAsync(HttpMethod.Delete, $"roles/{roleId:D}");
        refused.Status.ShouldBe(HttpStatusCode.Conflict);
        refused.Json["code"]!.GetValue<string>().ShouldBe("FCMS3044");
        refused.Json["detail"]!.GetValue<string>().ShouldContain("1 member group(s)");

        (await fixture.ManageAsync(HttpMethod.Delete, $"member-groups/{groupId}")).Status.ShouldBe(HttpStatusCode.NoContent);
        (await fixture.ManageAsync(HttpMethod.Delete, $"roles/{roleId:D}")).Status.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task AnAdministratorCreatesAMemberThatGetsALinkToChooseItsPassword()
    {
        var address = MemberFixture.NewAddress();

        var created = await fixture.ManageAsync(HttpMethod.Post, "members", $$"""{ "email": "{{address}}", "displayName": "Made by an admin" }""");
        created.Status.ShouldBe(HttpStatusCode.Created, created.Body?.ToJsonString());
        var id = created.Json["id"]!.GetValue<string>();
        created.Headers.Location!.ToString().ShouldBe($"{MemberFixture.Management}members/{id}");
        created.Json["emailVerified"]!.GetValue<bool>().ShouldBeFalse();
        created.Json["displayName"]!.GetValue<string>().ShouldBe("Made by an admin");
        created.Json.ToJsonString().ShouldNotContain("password", Case.Insensitive);

        (await fixture.ManageAsync(HttpMethod.Post, "members", $$"""{ "email": "{{address.ToUpperInvariant()}}" }""")).Json["code"]!.GetValue<string>().ShouldBe("FCMS3163");
        (await fixture.ManageAsync(HttpMethod.Post, "members", """{ "email": "not an address" }""")).Json["code"]!.GetValue<string>().ShouldBe("FCMS3164");

        // The link goes to the page that sets a password, and using it verifies the address too.
        var link = fixture.Emails.LinkTo(address);
        link.GetLeftPart(UriPartial.Path).ShouldBe("https://localhost/account/members/reset-password");
        (await fixture.DirectLoginAsync(address, Password)).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await fixture.MembersAsync(HttpMethod.Post, "reset-password", $$"""{ "email": "{{address}}", "token": "{{fixture.TokenOf(address)}}", "password": "{{Password}}" }""")).Status.ShouldBe(HttpStatusCode.NoContent);
        (await fixture.DirectLoginAsync(address, Password)).Status.ShouldBe(HttpStatusCode.OK);
        (await fixture.ManageAsync(HttpMethod.Get, $"members/{id}")).Json["emailVerified"]!.GetValue<bool>().ShouldBeTrue();

        var rows = await fixture.ActivityAsync("member.", id);
        var registered = rows.Single(row => row.Action == "member.registered");
        registered.ActorType.ShouldBe(PrincipalKind.AdminUser);
        registered.SiteId.ShouldBeNull();
        rows.Single(row => row.Action == "member.verified").ActorType.ShouldBe(PrincipalKind.Member);
    }

    [Fact]
    public async Task AMemberIsListedChangedDeactivatedReactivatedAndDeletedAndEachChangeIsAnActivityRow()
    {
        var tag = Guid.NewGuid().ToString("N")[..12];
        var first = await fixture.Host.CreateMemberAsync($"{tag}-one@example.com", groups: ["gold"], cancellationToken: Token);
        var second = await fixture.Host.CreateMemberAsync($"{tag}-two@example.com", cancellationToken: Token);
        var pending = $"{tag}-three@example.com";
        (await fixture.MembersAsync(HttpMethod.Post, "register", $$"""{ "email": "{{pending}}", "password": "{{Password}}" }""")).Status.ShouldBe(HttpStatusCode.Accepted);

        (await ListAsync($"search={tag}")).ShouldBe([first.Email, second.Email, pending], ignoreOrder: true);
        (await ListAsync($"search={tag.ToUpperInvariant()}-T")).ShouldBe([second.Email, pending], ignoreOrder: true);
        (await ListAsync($"search={tag}&verified=false")).ShouldBe([pending]);
        (await ListAsync($"search={tag}&verified=true")).ShouldBe([first.Email, second.Email], ignoreOrder: true);
        (await ListAsync($"search={tag}&group={fixture.Gold:D}")).ShouldBe([first.Email]);
        string[] pages = [.. await ListAsync($"search={tag}&pageSize=1&page=1"), .. await ListAsync($"search={tag}&pageSize=1&page=2"), .. await ListAsync($"search={tag}&pageSize=1&page=3")];
        pages.ShouldBe([first.Email, second.Email, pending], ignoreOrder: true);
        (await fixture.ManageAsync(HttpMethod.Get, $"members?search={tag}")).Json["total"]!.GetValue<long>().ShouldBe(3);
        (await fixture.ManageAsync(HttpMethod.Get, "members?verified=maybe")).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await fixture.ManageAsync(HttpMethod.Get, "members?role=admin")).Status.ShouldBe(HttpStatusCode.BadRequest);

        var read = await fixture.ManageAsync(HttpMethod.Get, $"members/{first.Id:D}");
        read.Json["email"]!.GetValue<string>().ShouldBe(first.Email);
        read.Json["groups"]!.AsArray().ShouldHaveSingleItem()!["id"]!.GetValue<string>().ShouldBe(fixture.Gold.ToString("D"));
        read.Json.ToJsonString().ShouldNotContain("passwordHash", Case.Insensitive);
        read.Json.ToJsonString().ShouldNotContain("securityStamp", Case.Insensitive);

        (await fixture.ManageAsync(HttpMethod.Patch, $"members/{first.Id:D}", """{ "displayName": "Renamed" }""")).Json["displayName"]!.GetValue<string>().ShouldBe("Renamed");
        (await fixture.ManageAsync(HttpMethod.Patch, $"members/{first.Id:D}", """{ "email": "moved@example.com" }""")).Status.ShouldBe(HttpStatusCode.UnprocessableEntity);

        (await fixture.ManageAsync(HttpMethod.Post, $"members/{first.Id:D}/deactivate")).Json["deactivatedAt"].ShouldNotBeNull();
        (await ListAsync($"search={tag}&active=false")).ShouldBe([first.Email]);
        (await ListAsync($"search={tag}&active=true")).ShouldBe([second.Email, pending], ignoreOrder: true);
        (await fixture.ManageAsync(HttpMethod.Post, $"members/{first.Id:D}/reactivate")).Json["deactivatedAt"].ShouldBeNull();

        (await fixture.ManageAsync(HttpMethod.Delete, $"members/{first.Id:D}")).Status.ShouldBe(HttpStatusCode.NoContent);
        (await fixture.ManageAsync(HttpMethod.Delete, $"members/{first.Id:D}")).Json["code"]!.GetValue<string>().ShouldBe("FCMS3161");
        (await ListAsync($"search={tag}")).ShouldBe([second.Email, pending], ignoreOrder: true);
        (await fixture.ManageAsync(HttpMethod.Get, $"member-groups/{fixture.Gold:D}")).Status.ShouldBe(HttpStatusCode.OK);

        var rows = await fixture.ActivityAsync("member.", first.Id.ToString("D"));
        rows.ShouldAllBe(row => row.SiteId == null && row.TargetType == "member" && row.TargetLabel == first.Email);
        rows.Where(row => row.Action is "member.updated" or "member.deactivated" or "member.deleted").ShouldAllBe(row => row.ActorType == PrincipalKind.AdminUser);
        rows.Select(row => row.Action).ShouldBe(["member.registered", "member.verified", "member.updated", "member.deactivated", "member.updated", "member.deleted"], ignoreOrder: true);
        rows.Where(row => row.Action == "member.updated").Select(row => Details(row.Details)["changes"]).ShouldBe(["displayName", "reactivated"], ignoreOrder: true);
    }

    [Fact]
    public async Task AnAdministratorListsAndEndsTheSessionsOfAMember()
    {
        var member = await fixture.Host.CreateMemberAsync(cancellationToken: Token);
        var tokens = await fixture.Host.SignInAsync(member);
        using var client = fixture.Bearer(tokens.AccessToken);
        (await fixture.MembersAsync(HttpMethod.Get, "me", client: client)).Status.ShouldBe(HttpStatusCode.OK);

        var sessions = await fixture.ManageAsync(HttpMethod.Get, $"members/{member.Id:D}/sessions");
        var session = sessions.Json["items"]!.AsArray().ShouldHaveSingleItem()!;
        session["clientId"]!.GetValue<string>().ShouldBe(FluentCmsTestHost.OAuthMemberClientId);
        var sessionId = session["id"]!.GetValue<string>();

        // A session of another member, or of an admin user, is not this member's to end.
        var other = await fixture.Host.CreateMemberAsync(cancellationToken: Token);
        (await fixture.ManageAsync(HttpMethod.Delete, $"members/{other.Id:D}/sessions/{sessionId}")).Json["code"]!.GetValue<string>().ShouldBe("FCMS3047");
        (await fixture.MembersAsync(HttpMethod.Get, "me", client: client)).Status.ShouldBe(HttpStatusCode.OK);

        (await fixture.ManageAsync(HttpMethod.Delete, $"members/{member.Id:D}/sessions/{sessionId}")).Status.ShouldBe(HttpStatusCode.NoContent);
        (await fixture.MembersAsync(HttpMethod.Get, "me", client: client)).Status.ShouldBe(HttpStatusCode.Unauthorized);
        (await fixture.RefreshAsync(tokens.RefreshToken!, FluentCmsTestHost.OAuthMemberClientId)).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await fixture.ManageAsync(HttpMethod.Get, $"members/{member.Id:D}/sessions")).Json["items"]!.AsArray().ShouldBeEmpty();

        // The users routes do not reach a member, nor the member routes a user.
        (await fixture.ManageAsync(HttpMethod.Get, $"users/{member.Id:D}")).Status.ShouldBe(HttpStatusCode.NotFound);
        var admin = await fixture.Host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        (await fixture.ManageAsync(HttpMethod.Get, $"members/{admin.Id:D}")).Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AMemberIsNotCreatedWhileItsLinkHasNowhereToGo()
    {
        var emails = new HostedPagesTests.CapturedEmails();
        await using var host = MemberFixture.NewHost(emails, configure: cms => cms.PublicOrigin = null);
        using var admin = await host.AsUserAsync(await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token));

        var refused = await MemberFixture.SendAsync(admin, HttpMethod.Post, MemberFixture.Management + "members", $$"""{ "email": "{{MemberFixture.NewAddress()}}" }""");
        refused.Status.ShouldBe(HttpStatusCode.Conflict);
        refused.Json["code"]!.GetValue<string>().ShouldBe("FCMS3173");
        (await MemberFixture.SendAsync(admin, HttpMethod.Get, MemberFixture.Management + "members")).Json["total"]!.GetValue<long>().ShouldBe(0);
        emails.Count.ShouldBe(0);
    }

    private async Task<string[]> ListAsync(string query)
    {
        var listed = await fixture.ManageAsync(HttpMethod.Get, "members?" + query);
        listed.Status.ShouldBe(HttpStatusCode.OK, listed.Body?.ToJsonString());
        return [.. listed.Json["items"]!.AsArray().Select(item => item!["email"]!.GetValue<string>())];
    }

    private static Dictionary<string, string?> Details(string? details) =>
        JsonSerializer.Deserialize<Dictionary<string, string?>>(details ?? "{}")!;
}
