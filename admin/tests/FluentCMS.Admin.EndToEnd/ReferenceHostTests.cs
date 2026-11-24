using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.EndToEnd;

// The host the browser tests drive, checked without a browser, so that these run wherever the tests build: started from its
// publish or its image in Production, seeded with its own commands, and serving the admin over https at its public origin.
public sealed class ReferenceHostTests(EndToEndHost running)
{
    private readonly EndToEndHost host = running.Running();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheHostIsMigratedAndItsEditorMadeWithItsOwnCommandsBeforeItStarts()
    {
        host.MigrateOutput.ShouldContain("Applied ");
        host.EditorCreated.ShouldStartWith($"Created the user {host.Editor.Email} (");
        host.EditorCreated.ShouldContain("with the role Editor.");

        using var client = host.Client();
        using var ready = await client.GetAsync(new Uri("health/ready", UriKind.Relative), Token);
        ready.StatusCode.ShouldBe(HttpStatusCode.OK);
        host.Sites.Count.ShouldBe(2);
        host.Sites[0].IsDefault.ShouldBeTrue();
        host.Sites[1].Name.ShouldBe("Second blog");
    }

    [Fact]
    public async Task TheAdminIsServedAtAdminWithItsConfigurationBuiltFromThePublicOrigin()
    {
        var origin = host.Origin.GetLeftPart(UriPartial.Authority);
        using var client = host.Client();

        using var page = await client.GetAsync(new Uri("admin/", UriKind.Relative), Token);
        page.StatusCode.ShouldBe(HttpStatusCode.OK);
        page.Headers.GetValues("Content-Security-Policy").ShouldHaveSingleItem().ShouldContain($"connect-src 'self' {origin};");
        (await page.Content.ReadAsStringAsync(Token)).ShouldContain("<base href=\"/admin/\" />");

        var configuration = await client.GetFromJsonAsync<JsonObject>(new Uri("admin/config.json", UriKind.Relative), Token);
        ((string?)configuration!["apiBaseUrl"]).ShouldBe(origin);
        ((string?)configuration["authority"]).ShouldBe(origin + "/");
        ((string?)configuration["clientId"]).ShouldBe("fluentcms-admin");
    }

    // The admin's client of the run, configured as the hosting package registers it for the public origin, with the addresses
    // of the static build beside them (EndToEndHost): authorize sends a request with PKCE to the hosted login, which answers
    // over https.
    [Fact]
    public async Task AuthorizeSendsTheAdminsSignInToTheHostedLoginOverHttps()
    {
        var callback = new Uri(host.Admin, "authentication/login-callback").AbsoluteUri;
        using var client = host.Client();
        var query = $"response_type=code&client_id=fluentcms-admin&redirect_uri={Uri.EscapeDataString(callback)}&scope=openid%20offline_access%20management"
            + "&state=e2e&code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGKSG2Mlqo&code_challenge_method=S256";

        using var authorize = await client.GetAsync(new Uri("connect/authorize?" + query, UriKind.Relative), Token);
        authorize.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var login = new Uri(host.Origin, authorize.Headers.Location!);
        login.AbsolutePath.ShouldBe("/account/login");

        using var page = await client.GetAsync(login, Token);
        page.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await page.Content.ReadAsStringAsync(Token)).ShouldContain("autocomplete=\"current-password\"");
    }
}
