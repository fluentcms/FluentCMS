using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentCMS.Agents;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Events;
using FluentCMS.Core.Jobs;
using FluentCMS.Diagnostics;
using FluentCMS.Plugins.Sample;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Mcp.Tests.McpClientTesting;
using static FluentCMS.Plugins.Mcp.Tests.McpTesting;

namespace FluentCMS.Plugins.Mcp.Tests;

// Dynamic client registration (RFC 7591) at /connect/register, under each policy, and what a client that registered
// itself gets: nothing until it is approved, and then what both the user's roles and its own role allow.
public sealed class McpClientRegistrationTests(McpClientFixture fixture) : IClassFixture<McpClientFixture>
{
    [Fact]
    public async Task AClientFindsTheEndpointInTheDiscoveryDocumentAndGetsAClientIdWithTheMetadataItSent()
    {
        var host = fixture.Host;
        await SetPolicyAsync(host, McpClientRegistrationMode.Approval);
        using var agent = new McpAgent(host, "10.2.0.1");
        using var admin = await KeyAsync(host);

        using var discovery = await agent.Http.GetAsync(new Uri("/.well-known/openid-configuration", UriKind.Relative), Token);
        var endpoint = (await JsonAsync(discovery))["registration_endpoint"]!.GetValue<string>();
        endpoint.ShouldBe("https://localhost/connect/register");

        using var content = new StringContent(
            """{ "client_name": "Desk agent", "redirect_uris": ["http://127.0.0.1:33418/callback", "https://agent.example.com/cb"], "grant_types": ["authorization_code", "refresh_token"], "response_types": ["code"], "token_endpoint_auth_method": "none", "scope": "openid mcp offline_access", "client_uri": "https://agent.example.com" }""",
            System.Text.Encoding.UTF8,
            "application/json");
        using var response = await agent.Http.PostAsync(new Uri(endpoint), content, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var registered = await JsonAsync(response);
        var clientId = registered["client_id"]!.GetValue<string>();
        clientId.ShouldStartWith("mcp-");
        registered["client_id_issued_at"]!.GetValue<long>().ShouldBe(host.Time.GetUtcNow().ToUnixTimeSeconds());
        registered["client_name"]!.GetValue<string>().ShouldBe("Desk agent");
        registered["redirect_uris"]!.AsArray().Select(uri => uri!.GetValue<string>()).ShouldBe(["http://127.0.0.1:33418/callback", "https://agent.example.com/cb"]);
        registered["grant_types"]!.AsArray().Select(grant => grant!.GetValue<string>()).ShouldBe(["authorization_code", "refresh_token"]);
        registered["response_types"]!.AsArray().Select(type => type!.GetValue<string>()).ShouldBe(["code"]);
        registered["token_endpoint_auth_method"]!.GetValue<string>().ShouldBe("none");
        registered["scope"]!.GetValue<string>().ShouldBe("mcp openid offline_access");
        registered.AsObject().ContainsKey("client_secret").ShouldBeFalse();
        registered.AsObject().ContainsKey("client_uri").ShouldBeFalse();

        var listed = (await FindAsync(admin, clientId)).ShouldNotBeNull();
        listed["name"]!.GetValue<string>().ShouldBe("Desk agent");
        listed["status"]!.GetValue<string>().ShouldBe("pending");
        listed["roleId"].ShouldBeNull();
        listed["redirectUris"]!.AsArray().Count.ShouldBe(2);
        listed["scopes"]!.AsArray().Select(scope => scope!.GetValue<string>()).ShouldBe(["openid", "mcp", "offline_access"]);
        listed["registeredAt"]!.GetValue<DateTimeOffset>().ShouldBe(host.Time.GetUtcNow());
        listed["lastActivityAt"].ShouldBeNull();
    }

    [Fact]
    public async Task WithApprovalThePendingClientIsRefusedAtTheAuthorizeEndpointAndGetsNoTokenUntilItIsApprovedWithARole()
    {
        var host = fixture.Host;
        await SetPolicyAsync(host, McpClientRegistrationMode.Approval);
        using var agent = new McpAgent(host, "10.2.0.2");
        using var admin = await KeyAsync(host);
        var user = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        var clientId = await agent.RegisteredAsync();
        var id = await IdOfAsync(admin, clientId);

        // Before the user is even asked to sign in, and again once signed in: the hosted error page, never the client.
        var unsigned = await agent.AuthorizeAsync(clientId);
        await agent.SignInAsync(user);
        var pending = await agent.AuthorizeAsync(clientId);
        using var page = await agent.Http.GetAsync(new Uri("/account/error?error=client_pending_approval", UriKind.Relative), Token);
        using var consent = await agent.ConsentAsync($"/account/consent?client_id={clientId}&scope=openid%20mcp", allow: true);

        unsigned.ShouldBe(new Authorized(null, "client_pending_approval", "/account/error", unsigned.Verifier));
        pending.ShouldBe(new Authorized(null, "client_pending_approval", "/account/error", pending.Verifier));
        page.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await page.Content.ReadAsStringAsync(Token)).ShouldContain("waits for an administrator to approve it");

        // No consent is recorded for a client that is not approved.
        consent.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        consent.Headers.Location!.OriginalString.ShouldStartWith("/account/error?");

        using var noRole = await ApproveAsync(admin, id, role: null);
        using var unknownRole = await ApproveAsync(admin, id, "No such role");
        noRole.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(noRole)).ShouldBe("FCMS1605");
        unknownRole.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(unknownRole)).ShouldBe("FCMS1606");
        (await agent.AuthorizeAsync(clientId)).Error.ShouldBe("client_pending_approval");

        using var approved = await ApproveAsync(admin, id, BuiltInRoles.EditorName);
        approved.StatusCode.ShouldBe(HttpStatusCode.OK);
        var client = await JsonAsync(approved);
        client["status"]!.GetValue<string>().ShouldBe("approved");
        client["roleId"]!.GetValue<Guid>().ShouldBe(BuiltInRoles.Editor);
        client["roleName"]!.GetValue<string>().ShouldBe(BuiltInRoles.EditorName);
        client["approvedAt"]!.GetValue<DateTimeOffset>().ShouldBe(host.Time.GetUtcNow());
        client["approvedBy"]!.GetValue<Guid>().ShouldNotBe(Guid.Empty);

        // The user consents on the hosted page, and the client gets its tokens.
        var refused = await agent.AuthorizeAsync(clientId, allow: false);
        var tokens = await agent.TokensAsync(clientId, user);
        refused.Error.ShouldBe("access_denied");
        tokens["access_token"]!.GetValue<string>().ShouldNotBeNullOrEmpty();
        tokens["refresh_token"]!.GetValue<string>().ShouldNotBeNullOrEmpty();

        using var again = await ApproveAsync(admin, id, BuiltInRoles.EditorName);
        using var rejected = await RejectAsync(admin, id);
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(again)).ShouldBe("FCMS1604");
        rejected.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(rejected)).ShouldBe("FCMS1604");
    }

    [Fact]
    public async Task ARejectedClientIsRefusedForGood()
    {
        var host = fixture.Host;
        await SetPolicyAsync(host, McpClientRegistrationMode.Approval);
        using var agent = new McpAgent(host, "10.2.0.3");
        using var admin = await KeyAsync(host);
        var clientId = await agent.RegisteredAsync();
        var id = await IdOfAsync(admin, clientId);

        using var rejected = await RejectAsync(admin, id);
        using var approved = await ApproveAsync(admin, id, BuiltInRoles.EditorName);
        await agent.SignInAsync(await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token));
        var authorized = await agent.AuthorizeAsync(clientId);

        rejected.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JsonAsync(rejected))["status"]!.GetValue<string>().ShouldBe("rejected");
        approved.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        authorized.ShouldBe(new Authorized(null, "client_rejected", "/account/error", authorized.Verifier));
    }

    [Fact]
    public async Task TheTokenWorksAtMcpLimitedToWhatBothTheUsersRolesAndTheClientsRoleAllow()
    {
        var host = fixture.Host;
        await SetPolicyAsync(host, McpClientRegistrationMode.Approval);
        using var agent = new McpAgent(host, "10.2.0.4");
        using var admin = await KeyAsync(host);
        var readerName = "Reader " + Guid.NewGuid().ToString("N")[..6];
        var reader = await host.CreateRoleAsync(readerName, ["content.article.read"], Token);
        var superAdmin = await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token);
        var readingUser = await host.CreateUserAsync(readerName, cancellationToken: Token);
        await host.As(superAdmin).Content<Article>().CreateAsync(new Article { Title = "Seen by agents" }, Token);

        // A Super Admin through a client that reads: reads only.
        var (limited, _) = await ApprovedAsync(agent, admin, reader.ToString("D"), "Reading agent");
        var limitedTokens = await agent.TokensAsync(limited, superAdmin);
        using (var http = agent.Bearer(limitedTokens["access_token"]!.GetValue<string>()))
        {
            await using var mcp = await ConnectAsync(http, protocolVersion: InitializeProtocol);

            var who = await mcp.CallToolAsync(SampleTools.WhoAmI, cancellationToken: Token);
            var titles = await mcp.CallToolAsync(SampleTools.EntryTitles, new Dictionary<string, object?> { ["type"] = "article", ["status"] = "draft" }, cancellationToken: Token);
            var created = await mcp.CallToolAsync(SampleTools.EntryCreate, new Dictionary<string, object?> { ["type"] = "article", ["title"] = "Not allowed" }, cancellationToken: Token);
            var tools = await mcp.ListToolsAsync(cancellationToken: Token);

            who.StructuredContent.ShouldNotBeNull().GetProperty("clientId").GetString().ShouldBe(limited);
            who.StructuredContent.ShouldNotBeNull().GetProperty("principal").GetGuid().ShouldBe(superAdmin.Id);
            titles.IsError.ShouldBe(false, Text(titles));
            Text(titles).ShouldContain("Seen by agents");
            created.IsError.ShouldBe(true);
            Code(created).ShouldBe("FCMS3103");
            tools.Select(tool => tool.Name).ShouldNotContain(SampleTools.NoteAdd);

            // The token is for /mcp: the Management API refuses it, whatever the roles.
            using var management = await http.GetAsync(new Uri(ClientsPath, UriKind.Relative), Token);
            management.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        // A refreshed token carries the same limit.
        using var refreshed = await agent.RefreshAsync(limited, limitedTokens["refresh_token"]!.GetValue<string>());
        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var http = agent.Bearer((await JsonAsync(refreshed))["access_token"]!.GetValue<string>()))
        {
            await using var mcp = await ConnectAsync(http, protocolVersion: InitializeProtocol);
            var created = await mcp.CallToolAsync(SampleTools.EntryCreate, new Dictionary<string, object?> { ["type"] = "article", ["title"] = "Still not" }, cancellationToken: Token);
            Code(created).ShouldBe("FCMS3103");
        }

        // A client with a wide role gives a user who reads nothing more, and a Super Admin through it writes.
        var (wide, _) = await ApprovedAsync(agent, admin, BuiltInRoles.SuperAdminName, "Wide agent");
        var readerTokens = await agent.TokensAsync(wide, readingUser);
        using (var http = agent.Bearer(readerTokens["access_token"]!.GetValue<string>()))
        {
            await using var mcp = await ConnectAsync(http, protocolVersion: InitializeProtocol);
            var created = await mcp.CallToolAsync(SampleTools.EntryCreate, new Dictionary<string, object?> { ["type"] = "article", ["title"] = "Not mine" }, cancellationToken: Token);
            Code(created).ShouldBe("FCMS3103");
        }

        var adminTokens = await agent.TokensAsync(wide, superAdmin);
        using (var http = agent.Bearer(adminTokens["access_token"]!.GetValue<string>()))
        {
            await using var mcp = await ConnectAsync(http, protocolVersion: InitializeProtocol);
            var created = await mcp.CallToolAsync(SampleTools.EntryCreate, new Dictionary<string, object?> { ["type"] = "article", ["title"] = "By both" }, cancellationToken: Token);
            created.IsError.ShouldBe(false, Text(created));
            (await mcp.ListToolsAsync(cancellationToken: Token)).Select(tool => tool.Name).ShouldContain(SampleTools.NoteAdd);
        }
    }

    // The tool list says what a call would allow (#2979), so for a client that registered itself it holds the tools and
    // the fields that the user's roles and the client's role both give, which is what a user of that role alone is listed.
    [Fact]
    public async Task TheToolListOfARegisteredClientHoldsOnlyTheToolsAndFieldsBothTheUserAndTheClientsRoleAllow()
    {
        await using var host = new FluentCmsTestHost()
            .WithContentTypes<Article, Page>()
            .WithServices(services => services.PostConfigure<RateLimitOptions>(limits => limits.Auth.PermitLimit = 10_000))
            .Configure(cms =>
            {
                cms.Jobs.Options.Queues[JobRunnerOptions.DefaultQueue] = 0;
                cms.Jobs.Options.Queues[OutboxDrainJob.Queue] = 0;
            });
        await SetPolicyAsync(host, McpClientRegistrationMode.Approval);
        using var agent = new McpAgent(host, "10.2.0.13");
        using var admin = await KeyAsync(host);
        var drafter = "Drafter " + Guid.NewGuid().ToString("N")[..6];
        using var role = await admin.PostAsJsonAsync(
            new Uri(McpToolsTesting.Roles, UriKind.Relative),
            new { name = drafter, grants = new[] { McpToolsTesting.Grant("content.page.read"), McpToolsTesting.Grant("content.page.update", "title"), McpToolsTesting.Grant("content.article.read") } },
            Token);
        role.StatusCode.ShouldBe(HttpStatusCode.Created, await role.Content.ReadAsStringAsync(Token));
        var superAdmin = await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token);
        var (clientId, _) = await ApprovedAsync(agent, admin, drafter, "Drafting agent");
        var tokens = await agent.TokensAsync(clientId, superAdmin);

        using var throughClient = agent.Bearer(tokens["access_token"]!.GetValue<string>());
        using var asSuperAdmin = await host.AsUserAsync(superAdmin, "openid", McpScope);
        using var asDrafter = await host.AsUserAsync(await host.CreateUserAsync(drafter, cancellationToken: Token), "openid", McpScope);
        await using var limitedClient = await ConnectAsync(throughClient, protocolVersion: InitializeProtocol);
        await using var ownClient = await ConnectAsync(asSuperAdmin, protocolVersion: InitializeProtocol);
        await using var drafterClient = await ConnectAsync(asDrafter, protocolVersion: InitializeProtocol);
        var limited = await McpToolsTesting.ToolsAsync(limitedClient);
        var own = await McpToolsTesting.ToolsAsync(ownClient);
        var ofDrafter = await McpToolsTesting.ToolsAsync(drafterClient);

        // Without the client, the Super Admin publishes, deletes and writes every field.
        own.Keys.ShouldContain(McpContentTools.Publish);
        own.Keys.ShouldContain("page_delete");
        own.Keys.ShouldContain("article_create_draft");
        UpdatableFields(own["page_update_draft"]).ShouldBe(["title", "notes"], ignoreOrder: true);

        // Through the client, whose role has no publish permission and updates the title of a page only.
        limited.Keys.ShouldNotContain(McpContentTools.Publish);
        limited.Keys.ShouldNotContain(McpContentTools.Unpublish);
        limited.Keys.ShouldNotContain(McpContentTools.Schedule);
        limited.Keys.ShouldNotContain("page_delete");
        limited.Keys.ShouldNotContain("page_create_draft");
        limited.Keys.ShouldNotContain("article_update_draft");
        limited.Keys.ShouldContain("page_update_draft");
        limited.Keys.ShouldContain("article_get");
        limited.Keys.ShouldContain(McpContentTools.Query);
        UpdatableFields(limited["page_update_draft"]).ShouldBe(["title"]);
        limited.Keys.ShouldBe(ofDrafter.Keys, ignoreOrder: true);
    }

    // The role of a client that is deleted afterwards leaves its tokens nothing, rather than the user's own reach.
    [Fact]
    public async Task AClientWhoseRoleIsGoneReachesNothing()
    {
        var host = fixture.Host;
        await SetPolicyAsync(host, McpClientRegistrationMode.Approval);
        using var agent = new McpAgent(host, "10.2.0.5");
        using var admin = await KeyAsync(host);
        var role = await host.CreateRoleAsync("Gone " + Guid.NewGuid().ToString("N")[..6], ["content.article.*"], Token);
        var user = await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token);
        var (clientId, _) = await ApprovedAsync(agent, admin, role.ToString("D"));
        var tokens = await agent.TokensAsync(clientId, user);
        using var http = agent.Bearer(tokens["access_token"]!.GetValue<string>());
        await using var mcp = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        var before = await mcp.CallToolAsync(SampleTools.EntryCreate, new Dictionary<string, object?> { ["type"] = "article", ["title"] = "While it lasts" }, cancellationToken: Token);
        await DeleteRoleAsync(role);
        var after = await mcp.CallToolAsync(SampleTools.EntryCreate, new Dictionary<string, object?> { ["type"] = "article", ["title"] = "No more" }, cancellationToken: Token);

        before.IsError.ShouldBe(false, Text(before));
        Code(after).ShouldBe("FCMS3103");
        (await FindAsync(admin, clientId)).ShouldNotBeNull()["roleName"].ShouldBeNull();
    }

    [Theory]
    [InlineData("""{ "redirect_uris": ["https://agent.example.com/cb"], "token_endpoint_auth_method": "client_secret_basic" }""", "invalid_client_metadata")]
    [InlineData("""{ "redirect_uris": ["https://agent.example.com/cb"], "scope": "mcp management" }""", "invalid_client_metadata")]
    [InlineData("""{ "redirect_uris": ["https://agent.example.com/cb"], "grant_types": ["authorization_code", "client_credentials"] }""", "invalid_client_metadata")]
    [InlineData("""{ "redirect_uris": ["http://agent.example.com/cb"] }""", "invalid_redirect_uri")]
    [InlineData("""{ "redirect_uris": ["https://*.example.com/cb"] }""", "invalid_redirect_uri")]
    [InlineData("""{ "redirect_uris": ["https://agent.example.com/cb#here"] }""", "invalid_redirect_uri")]
    [InlineData("""{ "redirect_uris": ["editor://callback"] }""", "invalid_redirect_uri")]
    [InlineData("""not json""", "invalid_client_metadata")]
    public async Task ARegistrationThatAsksForMoreThanAPublicMcpClientGetsIsRefused(string body, string error)
    {
        var host = fixture.Host;
        await SetPolicyAsync(host, McpClientRegistrationMode.Open, BuiltInRoles.EditorName);
        using var agent = new McpAgent(host, "10.2.0.6");
        using var admin = await KeyAsync(host);
        var before = (await ListAsync(admin)).Count;

        using var response = await agent.RegisterAsync(body);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var refusal = await JsonAsync(response);
        refusal["error"]!.GetValue<string>().ShouldBe(error);
        refusal["error_description"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace();
        (await ListAsync(admin)).Count.ShouldBe(before);
    }

    [Fact]
    public async Task ARegisteredClientGetsNoManagementScopeAndNoOtherGrantOrRedirectUri()
    {
        var host = fixture.Host;
        await SetPolicyAsync(host, McpClientRegistrationMode.Open, BuiltInRoles.SuperAdminName);
        using var agent = new McpAgent(host, "10.2.0.7");
        var clientId = await agent.RegisteredAsync();
        await agent.SignInAsync(await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token));

        var management = await agent.AuthorizeAsync(clientId, "openid management");
        var draft = await agent.AuthorizeAsync(clientId, "openid draft");
        var elsewhere = await agent.AuthorizeAsync(clientId, redirectUri: "http://127.0.0.1:33418/other");
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials", ["client_id"] = clientId, ["scope"] = "mcp" });
        using var credentials = await agent.Http.PostAsync(new Uri("/connect/token", UriKind.Relative), form, Token);

        management.Code.ShouldBeNull();
        management.Error.ShouldNotBeNull();
        draft.Code.ShouldBeNull();
        draft.Error.ShouldNotBeNull();
        elsewhere.Code.ShouldBeNull();
        elsewhere.StoppedAt.ShouldBe("/account/error");
        credentials.IsSuccessStatusCode.ShouldBeFalse();
    }

    [Fact]
    public async Task WithOpenARegistrationIsApprovedAtOnceWithTheDefaultRoleAndTheLimitPerAddressHolds()
    {
        var host = fixture.Host;
        await SetPolicyAsync(host, McpClientRegistrationMode.Open, BuiltInRoles.EditorName, perHour: 2);
        using var agent = new McpAgent(host, "10.2.0.8");
        using var neighbour = new McpAgent(host, "10.2.0.9");
        using var admin = await KeyAsync(host);
        var user = await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token);

        var clientId = await agent.RegisteredAsync("Open agent");
        var listed = (await FindAsync(admin, clientId)).ShouldNotBeNull();
        var tokens = await agent.TokensAsync(clientId, user);

        listed["status"]!.GetValue<string>().ShouldBe("approved");
        listed["roleId"]!.GetValue<Guid>().ShouldBe(BuiltInRoles.Editor);
        listed["roleName"]!.GetValue<string>().ShouldBe(BuiltInRoles.EditorName);
        listed["approvedBy"].ShouldBeNull();
        tokens["access_token"]!.GetValue<string>().ShouldNotBeNullOrEmpty();

        // A refused registration spends nothing of the address's hour; the third that would register is turned away.
        using var invalid = await agent.RegisterAsync("""{ "redirect_uris": ["http://agent.example.com/cb"] }""");
        using var second = await agent.RegisterAsync(McpAgent.Registration());
        using var third = await agent.RegisterAsync(McpAgent.Registration());
        using var other = await neighbour.RegisterAsync(McpAgent.Registration());

        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        third.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await CodeAsync(third)).ShouldBe("FCMS3110");
        int.Parse(third.Headers.RetryAfter!.ToString(), System.Globalization.CultureInfo.InvariantCulture).ShouldBeInRange(1, 3600);
        other.StatusCode.ShouldBe(HttpStatusCode.Created);

        host.Time.Advance(TimeSpan.FromHours(1));
        using var later = await agent.RegisterAsync(McpAgent.Registration());
        later.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    // The instance has one authorization server, so the instance defaults of the area are the policy, and a site's own
    // settings change nothing of it.
    [Fact]
    public async Task AnAdministratorSetsThePolicyThroughTheSettingsOfTheManagementApi()
    {
        var host = fixture.Host;
        await SetPolicyAsync(host, McpClientRegistrationMode.Approval);
        using var agent = new McpAgent(host, "10.2.0.12");
        using var admin = await KeyAsync(host);

        using var instance = await PutSettingsAsync(admin, "/api/management/v1/settings/mcp/defaults", """{ "clientRegistration": "off" }""");
        using var site = await PutSettingsAsync(admin, "/api/management/v1/settings/mcp", $$"""{ "clientRegistration": "open", "defaultClientRole": "{{BuiltInRoles.EditorName}}" }""");
        using var refused = await agent.RegisterAsync(McpAgent.Registration());
        using var invalid = await PutSettingsAsync(admin, "/api/management/v1/settings/mcp/defaults", """{ "clientRegistration": "anything" }""");

        instance.IsSuccessStatusCode.ShouldBeTrue(await instance.Content.ReadAsStringAsync(Token));
        site.IsSuccessStatusCode.ShouldBeTrue(await site.Content.ReadAsStringAsync(Token));
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        invalid.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    // Open without a role to give is not open: a client that would get the user's whole reach waits instead.
    [Fact]
    public async Task WithOpenAndNoUsableDefaultRoleARegistrationWaitsForApproval()
    {
        var host = fixture.Host;
        using var agent = new McpAgent(host, "10.2.0.10");
        using var admin = await KeyAsync(host);

        await SetPolicyAsync(host, McpClientRegistrationMode.Open);
        var unset = await agent.RegisteredAsync();
        await SetPolicyAsync(host, McpClientRegistrationMode.Open, "No such role");
        var unknown = await agent.RegisteredAsync();

        (await FindAsync(admin, unset)).ShouldNotBeNull()["status"]!.GetValue<string>().ShouldBe("pending");
        (await FindAsync(admin, unknown)).ShouldNotBeNull()["status"]!.GetValue<string>().ShouldBe("pending");
    }

    [Fact]
    public async Task TheStartupReportListsOpenRegistrationInProductionOnly()
    {
        var host = fixture.Host;
        var scopes = host.Services.GetRequiredService<IServiceScopeFactory>();
        var production = new McpInsecureDefaults(scopes, new Environment(Environments.Production));
        var development = new McpInsecureDefaults(scopes, new Environment(Environments.Development));

        await SetPolicyAsync(host, McpClientRegistrationMode.Open, BuiltInRoles.EditorName);
        var open = production.GetInsecureDefaults().ToList();
        var openInDevelopment = development.GetInsecureDefaults().ToList();
        await SetPolicyAsync(host, McpClientRegistrationMode.Approval);
        var approval = production.GetInsecureDefaults().ToList();

        open.ShouldHaveSingleItem().Id.ShouldBe("mcp-open-client-registration");
        open[0].Message.ShouldContain("mcp.clientRegistration");
        openInDevelopment.ShouldBeEmpty();
        approval.ShouldBeEmpty();
        host.Services.GetServices<IInsecureDefaultSource>().OfType<McpInsecureDefaults>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task WithOffTheEndpointAnswers404AndTheDiscoveryDocumentDoesNotNameIt()
    {
        var host = fixture.Host;
        await SetPolicyAsync(host, McpClientRegistrationMode.Off);
        using var agent = new McpAgent(host, "10.2.0.11");

        using var response = await agent.RegisterAsync(McpAgent.Registration());
        using var discovery = await agent.Http.GetAsync(new Uri("/.well-known/openid-configuration", UriKind.Relative), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(response)).ShouldBe("FCMS3104");
        (await JsonAsync(discovery)).AsObject().ContainsKey("registration_endpoint").ShouldBeFalse();
    }

    // Approval is what a host gets that sets nothing.
    [Fact]
    public void ApprovalIsTheDefaultPolicy()
    {
        var settings = new McpSettings();

        settings.ClientRegistration.ShouldBe(McpClientRegistrationMode.Approval);
        settings.DefaultClientRole.ShouldBeNull();
        settings.MaxPendingClients.ShouldBe(100);
        settings.RegistrationsPerHour.ShouldBe(10);
    }

    private static IEnumerable<string> UpdatableFields(ModelContextProtocol.Protocol.Tool tool) =>
        tool.InputSchema.GetProperty("properties").GetProperty("fields").GetProperty("properties").EnumerateObject().Select(property => property.Name);

    // A write of settings names the version it read, as the admin does.
    private static async Task<HttpResponseMessage> PutSettingsAsync(HttpClient admin, string path, string json)
    {
        using var read = await admin.GetAsync(new Uri(path, UriKind.Relative), Token);
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri(path, UriKind.Relative))
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("If-Match", read.Headers.ETag?.ToString());
        return await admin.SendAsync(request, Token);
    }

    private async Task DeleteRoleAsync(Guid role)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FluentCMS.Core.Storage.CmsDbContext>();
        context.Remove((await context.Set<FluentCMS.Core.Storage.Entities.Role>().FindAsync([role], Token))!);
        await context.SaveChangesAsync(Token);
    }

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
