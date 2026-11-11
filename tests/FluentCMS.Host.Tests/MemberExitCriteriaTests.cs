using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Shouldly;
using Xunit;

namespace FluentCMS.Host.Tests;

// The member half of the exit criteria of M5, on the reference host as the image runs it, with nothing set up but what
// environment variables set: a visitor registers on the member pages, verifies the address with the link of a mail that
// went over SMTP, signs in with the code flow and PKCE, and reads what the Member role allows. The member's token is
// refused by every surface that manages: the Management API, the AI assist routes and /mcp.
public sealed partial class MemberExitCriteriaTests : IAsyncDisposable
{
    private const string ShopClient = "shop";
    private const string ShopRedirect = "https://shop.example/signed-in";
    private const string Address = "mia@example.com";

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly HostUnderTest _host = new();
    private readonly FakeSmtpServer _smtp = new();

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        await _smtp.DisposeAsync();
    }

    [Fact]
    public async Task AVisitorRegistersVerifiesSignsInWithPkceAndReadsWhatTheMemberRoleAllows()
    {
        var password = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(18)) + "-aA1";
        var schema = _host.WriteFile("schema.json", """
            { "collections": [ { "name": "post", "fields": [ { "name": "title", "type": "text", "required": true, "maxLength": 200 } ] } ] }
            """);
        var settings = Operator.Settings();
        settings[ReferenceHost.SchemaVariable] = schema;
        settings["AllowedHosts"] = "localhost";
        settings["FluentCms:PublicOrigin"] = "https://localhost";
        settings["FluentCms:Email:Smtp:Host"] = "127.0.0.1";
        settings["FluentCms:Email:Smtp:Port"] = _smtp.Port.ToString(CultureInfo.InvariantCulture);
        settings["FluentCms:Email:Smtp:Security"] = "None";
        settings["FluentCms:Email:Smtp:From"] = "FluentCMS <cms@example.com>";
        settings["FluentCms:Members:AllowRegistration"] = "true";
        settings[$"FluentCms:Clients:Applications:{ShopClient}:Pool"] = "Members";
        settings[$"FluentCms:Clients:Applications:{ShopClient}:RedirectUris:0"] = ShopRedirect;
        settings[$"FluentCms:Clients:Applications:{ShopClient}:Scopes:0"] = "openid";
        settings[$"FluentCms:Clients:Applications:{ShopClient}:Scopes:1"] = "profile";
        await _host.StartAsync(settings);

        // The operator lets members read posts, and publishes one.
        using var operatorApi = await Operator.SignInAsync(_host);
        var memberRole = await Operator.RoleIdAsync(operatorApi, "Member");
        using (var content = new StringContent("""{ "name": "Member", "grants": [ { "permission": "content.post.read" } ] }""", Encoding.UTF8, "application/json"))
        using (var granted = await operatorApi.PutAsync(new Uri($"/api/management/v1/roles/{memberRole}", UriKind.Relative), content, Token))
        {
            granted.StatusCode.ShouldBe(HttpStatusCode.OK, await granted.Content.ReadAsStringAsync(Token));
        }

        Guid entry;
        using (var created = await Operator.PostJsonAsync(operatorApi, "/api/management/v1/content/post", new JsonObject { ["title"] = "For members" }))
        {
            var (status, body) = await HostUnderTest.ReadAsync(created);
            status.ShouldBe(HttpStatusCode.Created, body?.ToJsonString());
            entry = body!["id"]!.GetValue<Guid>();
        }

        using (var published = await operatorApi.PostAsync(new Uri($"/api/management/v1/content/post/{entry}/publish", UriKind.Relative), null, Token))
        {
            published.StatusCode.ShouldBe(HttpStatusCode.OK, await published.Content.ReadAsStringAsync(Token));
        }

        // The shop's request goes to the member login page; the visitor registers and verifies with the mailed link.
        var verifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var authorize = "/connect/authorize" + QueryString.Create(new Dictionary<string, string?>
        {
            ["client_id"] = ShopClient,
            ["response_type"] = "code",
            ["redirect_uri"] = ShopRedirect,
            ["scope"] = "openid profile",
            ["code_challenge"] = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),
            ["code_challenge_method"] = "S256",
            ["state"] = "exit",
        });

        using var browser = new Browser(_host.App.GetTestServer());
        using var challenge = await browser.GetAsync(authorize);
        challenge.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var login = challenge.Headers.Location!;
        login.AbsolutePath.ShouldBe("/account/members/login");

        using (var registered = await browser.SubmitAsync("/account/members/register", new Dictionary<string, string>
        {
            ["Input.Email"] = Address,
            ["Input.DisplayName"] = "Mia",
            ["Input.Password"] = password,
            ["Input.ConfirmPassword"] = password,
        }))
        {
            registered.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var verification = VerificationLink();
        verification.GetLeftPart(UriPartial.Path).ShouldBe("https://localhost/account/members/verify-email");
        using (var verified = await browser.SubmitAsync(verification.PathAndQuery, new Dictionary<string, string>()))
        {
            verified.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            verified.Headers.Location!.OriginalString.ShouldBe("/account/members/login?Status=email-verified");
        }

        using var signedIn = await browser.SubmitAsync(login.PathAndQuery, new Dictionary<string, string> { ["Input.Email"] = Address, ["Input.Password"] = password });
        using var back = await browser.FollowAsync(signedIn, ShopRedirect);
        back.StatusCode.ShouldBe(HttpStatusCode.Redirect, await back.Content.ReadAsStringAsync(Token));
        var code = QueryHelpers.ParseQuery(back.Headers.Location!.Query)["code"].ToString();
        code.ShouldNotBeNullOrEmpty();

        using var member = await Operator.BearerAsync(_host, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = ShopRedirect,
            ["client_id"] = ShopClient,
            ["code_verifier"] = verifier,
        });

        // The member reads its account and what the Member role allows, which a visitor does not.
        using (var me = await member.GetAsync(new Uri("/api/members/v1/me", UriKind.Relative), Token))
        {
            var (status, body) = await HostUnderTest.ReadAsync(me);
            status.ShouldBe(HttpStatusCode.OK, body?.ToJsonString());
            body!["email"]!.GetValue<string>().ShouldBe(Address);
            body["emailVerified"]!.GetValue<bool>().ShouldBeTrue();
            body["displayName"]!.GetValue<string>().ShouldBe("Mia");
        }

        using (var read = await member.GetAsync(new Uri($"/api/delivery/v1/post/{entry}", UriKind.Relative), Token))
        {
            var (status, body) = await HostUnderTest.ReadAsync(read);
            status.ShouldBe(HttpStatusCode.OK, body?.ToJsonString());
            body!["title"]!.GetValue<string>().ShouldBe("For members");
        }

        using var anonymous = _host.Client();
        using (var refused = await anonymous.GetAsync(new Uri($"/api/delivery/v1/post/{entry}", UriKind.Relative), Token))
        {
            refused.IsSuccessStatusCode.ShouldBeFalse();
        }

        // Every surface that manages refuses the member's token.
        foreach (var path in (string[])["/api/management/v1/me", "/api/management/v1/me/sites", "/api/management/v1/members", "/api/management/v1/content/post", "/api/management/v1/ai/usage"])
        {
            using var refused = await member.GetAsync(new Uri(path, UriKind.Relative), Token);
            refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, path);
        }

        using (var action = await Operator.PostJsonAsync(member, "/api/management/v1/ai/actions/rewrite", new JsonObject { ["type"] = "post", ["field"] = "title", ["text"] = "Ships are big boats." }))
        {
            action.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        using (var written = await Operator.PostJsonAsync(member, "/api/management/v1/content/post", new JsonObject { ["title"] = "By a member" }))
        {
            written.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        using (var mcp = await Operator.PostJsonAsync(member, "/mcp", new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "ping" }))
        {
            mcp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            mcp.Headers.WwwAuthenticate.ShouldHaveSingleItem().Parameter!.ShouldStartWith("error=\"insufficient_scope\"");
        }
    }

    // The verification mail as the SMTP server received it.
    private Uri VerificationLink()
    {
        using var message = FakeSmtpServer.Parse(_smtp.Messages.ShouldHaveSingleItem());
        message.To.Mailboxes.ShouldHaveSingleItem().Address.ShouldBe(Address);
        return new Uri(Link().Match(message.TextBody ?? "").Value);
    }

    [GeneratedRegex(@"https://\S+/account/members/verify-email\?\S+")]
    private static partial Regex Link();
}
