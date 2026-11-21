using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using FluentCMS.Agents;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Plugins.Sample;
using FluentCMS.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Mcp.Tests.McpClientTesting;
using static FluentCMS.Plugins.Mcp.Tests.McpTesting;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace FluentCMS.Plugins.Mcp.Tests;

// POST /mcp/clients, which registers a client for an agent that does not register itself, and PUT /mcp/clients/{id}/role,
// which changes the role of an approved client and ends its tokens.
public sealed class McpClientAdministrationTests(McpClientFixture fixture) : IClassFixture<McpClientFixture>
{
    [Fact]
    public async Task AClientAnAdministratorRegisteredSignsAUserInWithPkceAndConsentAndHoldsItsRole()
    {
        var host = fixture.Host;
        // Whether clients register themselves is not the administrator's concern.
        await SetPolicyAsync(host, McpClientRegistrationMode.Off);
        using var agent = new McpAgent(host, "10.4.0.1");
        var administrator = await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token);
        using var admin = await host.AsUserAsync(administrator);
        var readerName = "Reader " + Guid.NewGuid().ToString("N")[..6];
        var reader = await host.CreateRoleAsync(readerName, ["content.article.read"], Token);
        var superAdmin = await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token);
        await host.As(superAdmin).Content<Article>().CreateAsync(new Article { Title = "Read by desk agent" }, Token);

        using var response = await RegisterAsync(admin, Registration("Desk agent", readerName, McpAgent.RedirectUri, "https://agent.example.com/cb"));

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
        var client = await JsonAsync(response);
        var id = client["id"]!.GetValue<Guid>();
        var clientId = client["clientId"]!.GetValue<string>();
        response.Headers.Location!.OriginalString.ShouldBe($"mcp/clients/{id:D}");
        clientId.ShouldStartWith("mcp-");
        client["name"]!.GetValue<string>().ShouldBe("Desk agent");
        client["status"]!.GetValue<string>().ShouldBe("approved");
        client["roleId"]!.GetValue<Guid>().ShouldBe(reader);
        client["roleName"]!.GetValue<string>().ShouldBe(readerName);
        client["redirectUris"]!.AsArray().Select(uri => uri!.GetValue<string>()).ShouldBe([McpAgent.RedirectUri, "https://agent.example.com/cb"]);
        client["scopes"]!.AsArray().Select(scope => scope!.GetValue<string>()).ShouldBe(["openid", "email", "mcp", "offline_access", "profile"]);
        client["registeredAt"]!.GetValue<DateTimeOffset>().ShouldBe(host.Time.GetUtcNow());
        client["approvedAt"]!.GetValue<DateTimeOffset>().ShouldBe(host.Time.GetUtcNow());
        client["approvedBy"]!.GetValue<Guid>().ShouldBe(administrator.Id);
        client.AsObject().Select(member => member.Key).ShouldNotContain(key => key.Contains("secret", StringComparison.OrdinalIgnoreCase));
        (await FindAsync(admin, clientId)).ShouldNotBeNull()["status"]!.GetValue<string>().ShouldBe("approved");

        // The application a registration of the client itself would be.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            var application = (await applications.FindByClientIdAsync(clientId, Token)).ShouldNotBeNull();
            (await applications.HasClientTypeAsync(application, ClientTypes.Public, Token)).ShouldBeTrue();
            (await applications.HasConsentTypeAsync(application, ConsentTypes.Explicit, Token)).ShouldBeTrue();
            (await applications.HasRequirementAsync(application, Requirements.Features.ProofKeyForCodeExchange, Token)).ShouldBeTrue();
            (await applications.HasPermissionAsync(application, Permissions.Prefixes.Scope + "management", Token)).ShouldBeFalse();
        }

        // The user is asked, and a code is redeemed only with the verifier of its challenge.
        await agent.SignInAsync(superAdmin);
        var denied = await agent.AuthorizeAsync(clientId, allow: false);
        var allowed = await agent.AuthorizeAsync(clientId);
        using var wrongVerifier = await agent.RedeemAsync(clientId, allowed with { Verifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32)) });
        denied.Error.ShouldBe("access_denied");
        wrongVerifier.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await JsonAsync(wrongVerifier))["error"]!.GetValue<string>().ShouldBe("invalid_grant");

        // A Super Admin through a client that reads: reads only.
        var tokens = await agent.TokensAsync(clientId, superAdmin);
        using var http = agent.Bearer(tokens["access_token"]!.GetValue<string>());
        await using var mcp = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        var titles = await mcp.CallToolAsync(SampleTools.EntryTitles, new Dictionary<string, object?> { ["type"] = "article", ["status"] = "draft" }, cancellationToken: Token);
        var created = await mcp.CallToolAsync(SampleTools.EntryCreate, new Dictionary<string, object?> { ["type"] = "article", ["title"] = "Not allowed" }, cancellationToken: Token);
        titles.IsError.ShouldBe(false, Text(titles));
        Text(titles).ShouldContain("Read by desk agent");
        Code(created).ShouldBe("FCMS3103");
    }

    // The name and the redirect URIs are held to the rules of a client that registers itself, and the role is checked as on
    // approval. Nothing is registered for a refusal.
    [Fact]
    public async Task ARegistrationIsHeldToTheRulesOfARegistrationOfTheClientItself()
    {
        var host = fixture.Host;
        using var admin = await KeyAsync(host);
        var siteRoleName = "Site reader " + Guid.NewGuid().ToString("N")[..6];
        var siteRole = await SiteRoleAsync(host, siteRoleName);
        var before = (await ListAsync(admin)).Count;
        var eleven = Enumerable.Range(1, 11).Select(index => $"https://agent.example.com/{index}").ToArray();

        (string Body, string Code)[] refusals =
        [
            (Registration("Agent", BuiltInRoles.EditorName, "http://agent.example.com/cb"), "FCMS1608"),
            (Registration("Agent", BuiltInRoles.EditorName, "https://agent.example.com/*"), "FCMS1608"),
            (Registration("Agent", BuiltInRoles.EditorName, "https://agent.example.com/cb#part"), "FCMS1608"),
            (Registration("Agent", BuiltInRoles.EditorName, "https://user@agent.example.com/cb"), "FCMS1608"),
            (Registration("Agent", BuiltInRoles.EditorName, "/callback"), "FCMS1608"),
            (Registration("Agent", BuiltInRoles.EditorName), "FCMS1608"),
            (Registration("Agent", BuiltInRoles.EditorName, eleven), "FCMS1608"),
            ("""{ "name": "Agent", "role": "Editor" }""", "FCMS1608"),
            ("""{ "name": "Agent", "role": "Editor", "redirectUris": [null] }""", "FCMS1608"),
            (Registration("", BuiltInRoles.EditorName, McpAgent.RedirectUri), "FCMS1608"),
            (Registration("Line\nbreak", BuiltInRoles.EditorName, McpAgent.RedirectUri), "FCMS1608"),
            (Registration(new string('n', 129), BuiltInRoles.EditorName, McpAgent.RedirectUri), "FCMS1608"),
            (Registration("Agent", null, McpAgent.RedirectUri), "FCMS1605"),
            (Registration("Agent", " ", McpAgent.RedirectUri), "FCMS1605"),
            ("{}", "FCMS1605"),
            (Registration("Agent", "No such role", McpAgent.RedirectUri), "FCMS1606"),
            (Registration("Agent", siteRoleName, McpAgent.RedirectUri), "FCMS1606"),
        ];

        foreach (var (body, code) in refusals)
        {
            using var refused = await RegisterAsync(admin, body);
            refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, body);
            (await CodeAsync(refused)).ShouldBe(code, body);
        }

        (await ListAsync(admin)).Count.ShouldBe(before);

        // A role by its id, a role of a site included, and an instance-wide role by its name; a name without one is the
        // default name of a registration.
        using var byId = await RegisterAsync(admin, Registration("By id", siteRole.ToString("D"), McpAgent.RedirectUri));
        using var unnamed = await RegisterAsync(admin, Registration(null, BuiltInRoles.EditorName, "https://agent.example.com/cb", "https://agent.example.com/cb"));
        byId.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await JsonAsync(byId))["roleId"]!.GetValue<Guid>().ShouldBe(siteRole);
        unnamed.StatusCode.ShouldBe(HttpStatusCode.Created);
        var named = await JsonAsync(unnamed);
        named["name"]!.GetValue<string>().ShouldBe("MCP client");
        named["redirectUris"]!.AsArray().Count.ShouldBe(1);
    }

    [Fact]
    public async Task ChangingTheRoleEndsTheClientsTokensAndItsNextTokenCarriesTheNewRole()
    {
        var host = fixture.Host;
        await SetPolicyAsync(host, McpClientRegistrationMode.Approval);
        using var agent = new McpAgent(host, "10.4.0.3");
        using var admin = await KeyAsync(host);
        var readerName = "Reader " + Guid.NewGuid().ToString("N")[..6];
        await host.CreateRoleAsync(readerName, ["content.article.read"], Token);
        var superAdmin = await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token);
        var (clientId, id) = await RegisteredByAdminAsync(admin, readerName);
        var (otherId, _) = await ApprovedAsync(agent, admin, readerName, "Other agent");
        var tokens = await agent.TokensAsync(clientId, superAdmin);
        var other = await agent.TokensAsync(otherId, superAdmin);
        using var before = agent.Bearer(tokens["access_token"]!.GetValue<string>());
        using var otherHttp = agent.Bearer(other["access_token"]!.GetValue<string>());
        (await CreateAsync(before, "Before")).ShouldBe("FCMS3103");

        using var changed = await ChangeRoleAsync(admin, id, BuiltInRoles.SuperAdminName);

        changed.StatusCode.ShouldBe(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync(Token));
        var client = await JsonAsync(changed);
        client["status"]!.GetValue<string>().ShouldBe("approved");
        client["roleId"]!.GetValue<Guid>().ShouldBe(BuiltInRoles.SuperAdmin);
        client["roleName"]!.GetValue<string>().ShouldBe(BuiltInRoles.SuperAdminName);
        (await FindAsync(admin, clientId)).ShouldNotBeNull()["roleName"]!.GetValue<string>().ShouldBe(BuiltInRoles.SuperAdminName);

        // The access token is refused from the next request on, and so is the refresh token.
        using var cut = await PostAsync(before);
        using var refreshed = await agent.RefreshAsync(clientId, tokens["refresh_token"]!.GetValue<string>());
        cut.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await JsonAsync(refreshed))["error"]!.GetValue<string>().ShouldBe("invalid_grant");

        // The tokens of another client are not touched.
        using var untouched = await PostAsync(otherHttp);
        untouched.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The consent went with the old role, so the user is asked again; the other client keeps its consent.
        (await agent.AuthorizeAsync(clientId, allow: false)).Error.ShouldBe("access_denied");
        (await agent.AuthorizeAsync(otherId, allow: false)).Code.ShouldNotBeNull();

        // The next sign-in through the client gets a token with the new role, and its refresh keeps it.
        var after = await agent.TokensAsync(clientId, superAdmin);
        using (var http = agent.Bearer(after["access_token"]!.GetValue<string>()))
        {
            (await CreateAsync(http, "After")).ShouldBeNull();
        }

        using var renewed = await agent.RefreshAsync(clientId, after["refresh_token"]!.GetValue<string>());
        renewed.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var http = agent.Bearer((await JsonAsync(renewed))["access_token"]!.GetValue<string>()))
        {
            (await CreateAsync(http, "Refreshed")).ShouldBeNull();
        }
    }

    // Only an approved client changes its role; a pending one goes through approval. The role is checked as on approval,
    // and setting the role a client has changes nothing, its tokens included.
    [Fact]
    public async Task ARoleChangeIsRefusedForAClientThatIsNotApprovedOrARoleThatIsNotThere()
    {
        var host = fixture.Host;
        await SetPolicyAsync(host, McpClientRegistrationMode.Approval);
        using var agent = new McpAgent(host, "10.4.0.4");
        using var admin = await KeyAsync(host);
        var user = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        var pending = await IdOfAsync(admin, await agent.RegisteredAsync("Waiting agent"));
        var rejected = await IdOfAsync(admin, await agent.RegisteredAsync("Refused agent"));
        using var rejection = await RejectAsync(admin, rejected);
        var (clientId, approved) = await RegisteredByAdminAsync(admin, BuiltInRoles.EditorName);
        var tokens = await agent.TokensAsync(clientId, user);
        using var http = agent.Bearer(tokens["access_token"]!.GetValue<string>());

        using var ofPending = await ChangeRoleAsync(admin, pending, BuiltInRoles.EditorName);
        using var ofRejected = await ChangeRoleAsync(admin, rejected, BuiltInRoles.EditorName);
        using var unknownRole = await ChangeRoleAsync(admin, approved, "No such role");
        using var noRole = await ChangeRoleAsync(admin, approved, role: null);
        using var unknownClient = await ChangeRoleAsync(admin, Guid.NewGuid(), BuiltInRoles.EditorName);
        using var same = await ChangeRoleAsync(admin, approved, BuiltInRoles.Editor.ToString("D"));

        ofPending.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(ofPending)).ShouldBe("FCMS1607");
        ofRejected.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(ofRejected)).ShouldBe("FCMS1607");
        unknownRole.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(unknownRole)).ShouldBe("FCMS1606");
        noRole.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(noRole)).ShouldBe("FCMS1605");
        unknownClient.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(unknownClient)).ShouldBe("FCMS1603");
        same.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JsonAsync(same))["roleName"]!.GetValue<string>().ShouldBe(BuiltInRoles.EditorName);

        using var stillWorking = await PostAsync(http);
        stillWorking.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ListAsync(admin)).Single(item => item!["id"]!.GetValue<Guid>() == pending)!["status"]!.GetValue<string>().ShouldBe("pending");
        (await agent.AuthorizeAsync(await ClientIdOfAsync(admin, pending))).Error.ShouldBe("client_pending_approval");
    }

    // The OpenAPI document describes what the routes take and answer, as core's routes do.
    [Fact]
    public void TheRoutesDeclareTheirBodies()
    {
        var endpoints = fixture.Host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        var register = Route(endpoints, HttpMethods.Post, ClientsPath);
        var role = Route(endpoints, HttpMethods.Put, ClientsPath + "/{id:guid}/role");

        register.Metadata.OfType<IProducesResponseTypeMetadata>().ShouldContain(produced => produced.Type == typeof(McpClientInfo) && produced.StatusCode == StatusCodes.Status201Created);
        register.Metadata.OfType<IAcceptsMetadata>().ShouldContain(accepted => accepted.RequestType == typeof(McpClientRegistrationRequest));
        role.Metadata.OfType<IProducesResponseTypeMetadata>().ShouldContain(produced => produced.Type == typeof(McpClientInfo) && produced.StatusCode == StatusCodes.Status200OK);
        role.Metadata.OfType<IAcceptsMetadata>().ShouldContain(accepted => accepted.RequestType == typeof(McpClientRoleChange));

        var remove = Route(endpoints, HttpMethods.Post, ClientsPath + "/remove");
        remove.Metadata.OfType<IProducesResponseTypeMetadata>().ShouldContain(produced => produced.Type == typeof(McpClientRemovalResult) && produced.StatusCode == StatusCodes.Status200OK);
        remove.Metadata.OfType<IAcceptsMetadata>().ShouldContain(accepted => accepted.RequestType == typeof(McpClientRemoval));
    }

    private static RouteEndpoint Route(IEnumerable<RouteEndpoint> endpoints, string method, string pattern) =>
        endpoints.Single(endpoint => endpoint.RoutePattern.RawText?.Trim('/') == pattern.Trim('/')
            && endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(method) == true);

    private static async Task<string> ClientIdOfAsync(HttpClient admin, Guid id) =>
        (await ListAsync(admin)).Single(item => item!["id"]!.GetValue<Guid>() == id)!["clientId"]!.GetValue<string>();

    // The code of a create refused through the client, or null when it was allowed.
    private static async Task<string?> CreateAsync(HttpClient http, string title)
    {
        await using var mcp = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        var created = await mcp.CallToolAsync(SampleTools.EntryCreate, new Dictionary<string, object?> { ["type"] = "article", ["title"] = title }, cancellationToken: Token);
        return created.IsError == true ? Code(created) : null;
    }

    // A role of the default site, which is named by its id only, since a name is unique only within its site.
    private static async Task<Guid> SiteRoleAsync(FluentCmsTestHost host, string name)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        var role = new Role { Id = Guid.CreateVersion7(), Name = name, SiteId = DefaultSite.Id };
        context.Set<Role>().Add(role);
        await context.SaveChangesAsync(Token);
        return role.Id;
    }
}
