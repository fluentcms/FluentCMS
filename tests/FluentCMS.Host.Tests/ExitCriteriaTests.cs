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

// The exit criteria of M2, on the reference host as the image runs it: types from a mounted schema file, SQLite with no
// configuration, email over SMTP, and nothing set up but what environment variables set. The operator's service account
// invites an editor, who accepts on the hosted pages, signs in there, gets a token with PKCE and publishes through the
// Management API; a Delivery key then reads the entry.
public sealed partial class ExitCriteriaTests : IAsyncDisposable
{
    private const string EditorClient = "editor-app";
    private const string EditorRedirect = "https://app.example/signed-in";
    private const string Password = "Correct-Horse-Battery-9";

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly HostUnderTest _host = new();
    private readonly FakeSmtpServer _smtp = new();

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        await _smtp.DisposeAsync();
    }

    [Fact]
    public async Task AnInvitedEditorSignsInWithPkcePublishesAndADeliveryKeyReadsTheEntry()
    {
        var schema = _host.WriteFile("schema.json", """
            { "collections": [ { "name": "post", "fields": [ { "name": "title", "type": "text", "required": true, "maxLength": 200 }, { "name": "body", "type": "longText" } ] } ] }
            """);
        var settings = Operator.Settings();
        settings[ReferenceHost.SchemaVariable] = schema;
        settings["AllowedHosts"] = "localhost";
        settings["FluentCms:Email:Smtp:Host"] = "127.0.0.1";
        settings["FluentCms:Email:Smtp:Port"] = _smtp.Port.ToString(CultureInfo.InvariantCulture);
        settings["FluentCms:Email:Smtp:Security"] = "None";
        settings["FluentCms:Email:Smtp:From"] = "FluentCMS <cms@example.com>";
        settings[$"FluentCms:Clients:Applications:{EditorClient}:RedirectUris:0"] = EditorRedirect;
        settings[$"FluentCms:Clients:Applications:{EditorClient}:Scopes:0"] = "openid";
        settings[$"FluentCms:Clients:Applications:{EditorClient}:Scopes:1"] = "management";
        await _host.StartAsync(settings);

        using var operatorApi = await Operator.SignInAsync(_host);
        var editorRole = await Operator.RoleIdAsync(operatorApi, "Editor");
        using (var invited = await Operator.PostJsonAsync(operatorApi, "/api/management/v1/invitations", new JsonObject { ["email"] = "ada@example.com", ["roleIds"] = new JsonArray(editorRole) }))
        {
            invited.StatusCode.ShouldBe(HttpStatusCode.Created, await invited.Content.ReadAsStringAsync(Token));
        }

        var invitation = InvitationLink();
        invitation.GetLeftPart(UriPartial.Path).ShouldBe("https://localhost/account/accept-invitation");

        using var browser = new Browser(_host.App.GetTestServer());
        using (var accepted = await browser.SubmitAsync(invitation.PathAndQuery, new Dictionary<string, string>
        {
            ["Input.UserName"] = "ada",
            ["Input.Password"] = Password,
            ["Input.ConfirmPassword"] = Password,
        }))
        {
            accepted.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            accepted.Headers.Location!.OriginalString.ShouldBe("/account/login?Status=invitation-accepted");
        }

        var verifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var authorize = "/connect/authorize" + QueryString.Create(new Dictionary<string, string?>
        {
            ["client_id"] = EditorClient,
            ["response_type"] = "code",
            ["redirect_uri"] = EditorRedirect,
            ["scope"] = "openid management",
            ["code_challenge"] = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),
            ["code_challenge_method"] = "S256",
            ["state"] = "exit",
        });

        using var challenge = await browser.GetAsync(authorize);
        challenge.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var login = challenge.Headers.Location!;
        login.AbsolutePath.ShouldBe("/account/login");

        using var signedIn = await browser.SubmitAsync(login.PathAndQuery, new Dictionary<string, string> { ["Input.UserName"] = "ada", ["Input.Password"] = Password });
        using var back = await browser.FollowAsync(signedIn, EditorRedirect);
        back.StatusCode.ShouldBe(HttpStatusCode.Redirect, await back.Content.ReadAsStringAsync(Token));
        var code = QueryHelpers.ParseQuery(back.Headers.Location!.Query)["code"].ToString();
        code.ShouldNotBeNullOrEmpty();

        using var editorApi = await Operator.BearerAsync(_host, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = EditorRedirect,
            ["client_id"] = EditorClient,
            ["code_verifier"] = verifier,
        });

        Guid entry;
        using (var created = await Operator.PostJsonAsync(editorApi, "/api/management/v1/content/post", new JsonObject { ["title"] = "Hello from the image", ["body"] = "Published by an invited editor." }))
        {
            var (status, body) = await HostUnderTest.ReadAsync(created);
            status.ShouldBe(HttpStatusCode.Created, body?.ToJsonString());
            entry = body!["id"]!.GetValue<Guid>();
        }

        using (var published = await editorApi.PostAsync(new Uri($"/api/management/v1/content/post/{entry}/publish", UriKind.Relative), null, Token))
        {
            published.StatusCode.ShouldBe(HttpStatusCode.OK, await published.Content.ReadAsStringAsync(Token));
        }

        using var delivery = await Operator.DeliveryClientAsync(_host, operatorApi, "post");
        using (var read = await delivery.GetAsync(new Uri($"/api/delivery/v1/post/{entry}", UriKind.Relative), Token))
        {
            var (status, body) = await HostUnderTest.ReadAsync(read);
            status.ShouldBe(HttpStatusCode.OK, body?.ToJsonString());
            body!["title"]!.GetValue<string>().ShouldBe("Hello from the image");
        }

        using var anonymous = _host.Client();
        using var refused = await anonymous.GetAsync(new Uri($"/api/delivery/v1/post/{entry}", UriKind.Relative), Token);
        refused.IsSuccessStatusCode.ShouldBeFalse();
    }

    // The invitation as the SMTP server received it.
    private Uri InvitationLink()
    {
        using var message = FakeSmtpServer.Parse(_smtp.Messages.ShouldHaveSingleItem());
        message.To.Mailboxes.ShouldHaveSingleItem().Address.ShouldBe("ada@example.com");
        return new Uri(Link().Match(message.TextBody ?? "").Value);
    }

    [GeneratedRegex(@"https://\S+/account/accept-invitation\?\S+")]
    private static partial Regex Link();
}
