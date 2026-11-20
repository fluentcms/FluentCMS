using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Admin.Hosting;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Data.Sqlite;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Host.Tests;

// The admin of the image at /admin (admin spec 6.1): the app's files, index.html with its base set, config.json from
// FluentCms:Admin, the page's headers and the admin client.
public sealed class AdminHostingTests : IAsyncDisposable
{
    private const string Callback = "https://localhost/admin/authentication/login-callback";

    private const string Policy = "default-src 'none'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self' 'unsafe-inline'; "
        + "img-src 'self' data: blob: https://localhost; font-src 'self'; connect-src 'self' https://localhost; "
        + "base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly HostUnderTest _host = new();

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public async Task TheAdminIsOffByDefaultAndNothingUnderAdminAnswersOrRegistersAClient()
    {
        await _host.StartAsync(Operator.Settings());

        using var client = _host.Client();
        foreach (var path in new[] { "/admin", "/admin/", "/admin/index.html", "/admin/config.json", "/admin/_framework/blazor.webassembly.js", "/admin/system/components" })
        {
            using var response = await client.GetAsync(new Uri(path, UriKind.Relative), Token);
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound, path);
            response.Headers.Location.ShouldBeNull(path);
            response.Headers.Contains("Content-Security-Policy").ShouldBeFalse(path);
            (await response.Content.ReadAsByteArrayAsync(Token)).ShouldBeEmpty(path);
        }

        using var authorize = await client.GetAsync(Authorize(Callback, pkce: true), Token);
        ShouldBeRefused(authorize, "client_id");
    }

    [Fact]
    public async Task TheAppIsServedAtAdminWithItsBaseSetAndThePageHeaders()
    {
        await _host.StartAsync(AdminSettings());
        using var client = _host.Client();

        using var page = await client.GetAsync(new Uri("/admin/", UriKind.Relative), Token);
        page.StatusCode.ShouldBe(HttpStatusCode.OK);
        page.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        page.Headers.CacheControl!.NoStore.ShouldBeTrue();
        page.Headers.GetValues("Content-Security-Policy").ShouldHaveSingleItem().ShouldBe(Policy);
        page.Headers.GetValues("X-Content-Type-Options").ShouldHaveSingleItem().ShouldBe("nosniff");
        page.Headers.GetValues("Referrer-Policy").ShouldHaveSingleItem().ShouldBe("no-referrer");
        page.Headers.GetValues("Cross-Origin-Opener-Policy").ShouldHaveSingleItem().ShouldBe("same-origin");
        var html = await page.Content.ReadAsStringAsync(Token);

        // Only the href of <base> differs from the file the app was built with.
        html.ShouldBe(BuiltIndex().Replace("<base href=\"/\" />", "<base href=\"/admin/\" />", StringComparison.Ordinal));
        html.ShouldContain("<base href=\"/admin/\" />");

        // Client routes, the sign-in callback among them, get the same page, also as index.html with a compressed copy at hand.
        foreach (var path in new[] { "/admin/system/components", "/admin/authentication/login-callback?code=x&state=y", "/admin/index.html" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.AcceptEncoding.ParseAdd("gzip, br");
            using var route = await client.SendAsync(request, Token);
            route.StatusCode.ShouldBe(HttpStatusCode.OK, path);
            route.Content.Headers.ContentEncoding.ShouldBeEmpty(path);
            route.Headers.GetValues("Content-Security-Policy").ShouldHaveSingleItem().ShouldBe(Policy, path);
            (await route.Content.ReadAsStringAsync(Token)).ShouldBe(html, path);
        }

        // At /admin, or in another case, the app's base would not contain the page.
        foreach (var (path, canonical) in new[] { ("/admin?x=1", "/admin/?x=1"), ("/ADMIN/system/components?x=1", "/admin/system/components?x=1"), ("/Admin/index.html", "/admin/index.html") })
        {
            using var other = await client.GetAsync(new Uri(path, UriKind.Relative), Token);
            other.StatusCode.ShouldBe(HttpStatusCode.Redirect, path);
            other.Headers.Location!.OriginalString.ShouldBe(canonical, path);
        }

        // A segment escaped twice stays escaped in the address the redirect names, so the browser stays under the base.
        foreach (var (path, canonical) in new[] { ("/ADMIN/%252e%252e/health/live", "/admin/%252e%252e/health/live"), ("/ADMIN/%252e%252e//example.com/x", "/admin/%252e%252e//example.com/x") })
        {
            using var escaped = await client.GetAsync(new Uri(path, UriKind.Relative), Token);
            escaped.StatusCode.ShouldBe(HttpStatusCode.Redirect, path);
            escaped.Headers.Location!.OriginalString.ShouldBe(canonical, path);
            new Uri(new Uri("https://localhost" + path), escaped.Headers.Location).AbsolutePath.ShouldStartWith("/admin/", Case.Sensitive, path);
        }

        // HEAD answers the page's headers without its body, and the page takes no other method.
        using (var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/admin/system/components"), Token))
        {
            head.StatusCode.ShouldBe(HttpStatusCode.OK);
            head.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
            (await head.Content.ReadAsByteArrayAsync(Token)).ShouldBeEmpty();
        }

        foreach (var path in new[] { "/admin/", "/admin/system/components", "/admin/config.json" })
        {
            using var posted = await client.PostAsync(new Uri(path, UriKind.Relative), new StringContent(""), Token);
            posted.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed, path);
        }
    }

    // Served from the build's manifest, the files are not cached for good, so that a rebuild is seen; the published ones
    // are, which the Compose smoke test checks on the image.
    [Fact]
    public async Task TheFrameworkFilesAreServedWithTheirTypesCachingAndCompressionAndAMissingFileIsA404()
    {
        await _host.StartAsync(AdminSettings());
        using var client = _host.Client();

        var fingerprinted = FrameworkRoute(".wasm", immutable: true);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/" + fingerprinted);
        request.Headers.AcceptEncoding.ParseAdd("gzip");
        using var wasm = await client.SendAsync(request, Token);
        wasm.StatusCode.ShouldBe(HttpStatusCode.OK, fingerprinted);
        wasm.Content.Headers.ContentType!.MediaType.ShouldBe("application/wasm");
        wasm.Content.Headers.ContentEncoding.ShouldBe(["gzip"]);
        wasm.Headers.GetValues("X-Content-Type-Options").ShouldHaveSingleItem().ShouldBe("nosniff");

        using var script = await client.GetAsync(new Uri("/admin/_framework/blazor.webassembly.js", UriKind.Relative), Token);
        script.StatusCode.ShouldBe(HttpStatusCode.OK);
        script.Content.Headers.ContentType!.MediaType.ShouldBe("text/javascript");

        using var icu = await client.GetAsync(new Uri("/admin/_framework/icudt.dat", UriKind.Relative), Token);
        icu.StatusCode.ShouldBe(HttpStatusCode.OK);
        icu.Content.Headers.ContentType!.MediaType.ShouldBe("application/octet-stream");

        using var styles = await client.GetAsync(new Uri("/admin/_content/FluentCMS.Admin.Components/cms.css", UriKind.Relative), Token);
        styles.StatusCode.ShouldBe(HttpStatusCode.OK);

        foreach (var path in new[] { "/admin/_framework/missing.js", "/admin/_framework/missing", "/admin/_content/missing", "/admin/missing.png", "/admin/config.json.gz", "/_framework/blazor.webassembly.js" })
        {
            using var missing = await client.GetAsync(new Uri(path, UriKind.Relative), Token);
            missing.StatusCode.ShouldBe(HttpStatusCode.NotFound, path);
        }
    }

    [Fact]
    public async Task ConfigJsonCarriesTheHostsValuesAndNeverItsSampleOrARequestsHost()
    {
        var settings = AdminSettings();
        settings["FluentCms:Admin:Branding:Name"] = "Acme CMS";
        settings["FluentCms:Admin:Branding:LogoUrl"] = "https://cdn.example.com/logo.svg";
        settings["FluentCms:Admin:Branding:FaviconUrl"] = "images/favicon.png";
        settings["FluentCms:Admin:Branding:AccentColor"] = "#0f6cbd";
        settings["FluentCms:Admin:Cultures:Offered:0"] = "en";
        settings["FluentCms:Admin:Cultures:Offered:1"] = "fr";
        settings["FluentCms:Admin:Cultures:Default"] = "fr";
        settings["FluentCms:Admin:PreviewOrigins:0"] = "https://www.example.com/";
        await _host.StartAsync(settings);
        using var client = _host.Client();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/config.json");
        request.Headers.AcceptEncoding.ParseAdd("gzip, br");
        request.Headers.Host = "evil.example";
        using var response = await client.SendAsync(request, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        response.Content.Headers.ContentEncoding.ShouldBeEmpty();
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var (_, configuration) = await HostUnderTest.ReadAsync(response);

        configuration!.ToJsonString().ShouldBe(new JsonObject
        {
            ["apiBaseUrl"] = "https://localhost",
            ["authority"] = "https://localhost/",
            ["clientId"] = "fluentcms-admin",
            ["scopes"] = new JsonArray("openid", "profile", "email", "offline_access", "management"),
            ["branding"] = new JsonObject
            {
                ["name"] = "Acme CMS",
                ["logoUrl"] = "https://cdn.example.com/logo.svg",
                ["faviconUrl"] = "images/favicon.png",
                ["accentColor"] = "#0f6cbd",
            },
            ["cultures"] = new JsonObject { ["offered"] = new JsonArray("en", "fr"), ["default"] = "fr" },
            ["previewOrigins"] = new JsonArray("https://www.example.com"),
        }.ToJsonString());

        // The authority is the issuer of the discovery document, which the admin compares as an exact string.
        using var discovery = await client.GetAsync(new Uri("/.well-known/openid-configuration", UriKind.Relative), Token);
        var (_, metadata) = await HostUnderTest.ReadAsync(discovery);
        metadata!["issuer"]!.GetValue<string>().ShouldBe(configuration["authority"]!.GetValue<string>());

        using var page = await client.GetAsync(new Uri("/admin/", UriKind.Relative), Token);
        var policy = page.Headers.GetValues("Content-Security-Policy").ShouldHaveSingleItem();
        policy.ShouldContain("img-src 'self' data: blob: https://localhost https://cdn.example.com;");
        policy.ShouldContain("frame-src https://www.example.com;");
    }

    [Fact]
    public async Task UnderAPathBaseTheAdminIsServedThereAndNowhereElse()
    {
        var settings = AdminSettings();
        settings["FluentCms:ReverseProxy:PathBase"] = "/cms";
        await _host.StartAsync(settings);
        using var client = _host.Client();

        using var page = await client.GetAsync(new Uri("/cms/admin/", UriKind.Relative), Token);
        page.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await page.Content.ReadAsStringAsync(Token)).ShouldContain("<base href=\"/cms/admin/\" />");

        using var config = await client.GetAsync(new Uri("/cms/admin/config.json", UriKind.Relative), Token);
        var (_, configuration) = await HostUnderTest.ReadAsync(config);
        configuration!["apiBaseUrl"]!.GetValue<string>().ShouldBe("https://localhost/cms");

        using var script = await client.GetAsync(new Uri("/cms/admin/_framework/blazor.webassembly.js", UriKind.Relative), Token);
        script.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var bare = await client.GetAsync(new Uri("/cms/admin", UriKind.Relative), Token);
        bare.Headers.Location!.OriginalString.ShouldBe("/cms/admin/");
        using var cased = await client.GetAsync(new Uri("/CMS/admin/system/components", UriKind.Relative), Token);
        cased.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        cased.Headers.Location!.OriginalString.ShouldBe("/cms/admin/system/components");

        using var outside = await client.GetAsync(new Uri("/admin/", UriKind.Relative), Token);
        outside.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TheAdminClientIsPublicSignsInWithPkceAndOnlyToTheAdminsCallback()
    {
        await _host.StartAsync(AdminSettings());
        using var client = _host.Client();

        using var accepted = await client.GetAsync(Authorize(Callback, pkce: true), Token);
        accepted.StatusCode.ShouldBe(HttpStatusCode.Redirect, await accepted.Content.ReadAsStringAsync(Token));
        accepted.Headers.Location!.AbsolutePath.ShouldBe("/account/login");

        using var elsewhere = await client.GetAsync(Authorize("https://evil.example/admin/authentication/login-callback", pkce: true), Token);
        ShouldBeRefused(elsewhere, "redirect_uri");

        using var withoutPkce = await client.GetAsync(Authorize(Callback, pkce: false), Token);
        ShouldBeRefused(withoutPkce, "code_challenge");

        // A public client: no secret, so no client credentials either.
        using var token = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials", ["client_id"] = "fluentcms-admin" });
        using var refused = await client.PostAsync(new Uri("/connect/token", UriKind.Relative), token, Token);
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AConfiguredAdminClientWinsWholeOverTheRegistration()
    {
        var settings = AdminSettings();
        settings["FluentCms:Clients:Applications:fluentcms-admin:RedirectUris:0"] = "https://admin.example.com/authentication/login-callback";
        settings["FluentCms:Clients:Applications:fluentcms-admin:Scopes:0"] = "openid";
        settings["FluentCms:Clients:Applications:fluentcms-admin:Scopes:1"] = "management";
        await _host.StartAsync(settings);
        using var client = _host.Client();

        using var configured = await client.GetAsync(Authorize("https://admin.example.com/authentication/login-callback", pkce: true, scope: "openid management"), Token);
        configured.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        configured.Headers.Location!.AbsolutePath.ShouldBe("/account/login");

        // Not merged: the built-in callback is not registered beside the configured one.
        using var builtIn = await client.GetAsync(Authorize(Callback, pkce: true, scope: "openid management"), Token);
        ShouldBeRefused(builtIn, "redirect_uri");
    }

    [Fact]
    public async Task InDevelopmentConfigJsonFollowsTheRequestAndTheClientTheAddressesTheHostListensOn()
    {
        var settings = Operator.Settings();
        settings["FluentCms:Admin:Enabled"] = "true";
        settings["urls"] = "https://localhost:5443;http://*:5080";
        await _host.StartAsync(settings, environment: "Development");
        using var client = _host.Client(new Uri("https://localhost:5443/"));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/config.json");
        request.Headers.Host = "dev.example:8443";
        using var response = await client.SendAsync(request, Token);
        var (_, configuration) = await HostUnderTest.ReadAsync(response);
        configuration!["apiBaseUrl"]!.GetValue<string>().ShouldBe("https://dev.example:8443");

        // The script dotnet watch adds to the page reaches its own WebSocket.
        using var page = await client.GetAsync(new Uri("/admin/", UriKind.Relative), Token);
        page.Headers.GetValues("Content-Security-Policy").ShouldHaveSingleItem()
            .ShouldContain("connect-src 'self' https://localhost:5443 ws://localhost:* wss://localhost:*;");

        using var listening = await client.GetAsync(Authorize("https://localhost:5443/admin/authentication/login-callback", pkce: true), Token);
        listening.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        using var wildcard = await client.GetAsync(Authorize("http://localhost:5080/admin/authentication/login-callback", pkce: true), Token);
        wildcard.StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task OutsideDevelopmentTheAdminNeedsThePublicOrigin()
    {
        var settings = Operator.Settings();
        settings["FluentCms:Admin:Enabled"] = "true";

        var exception = await Should.ThrowAsync<Exception>(() => _host.StartAsync(settings));

        var refusal = Refusals(exception).ShouldHaveSingleItem();
        refusal.Code.ShouldBe("FCMS1101");
        refusal.Message.ShouldContain("FluentCms:PublicOrigin");
    }

    [Theory]
    [InlineData("ApiBaseUrl", "cms.example.com")]
    [InlineData("ApiBaseUrl", "https://cms.example.com/?x=1")]
    [InlineData("Branding:AccentColor", "red")]
    [InlineData("Branding:LogoUrl", "javascript:alert(1)")]
    [InlineData("Branding:Name", " Acme")]
    [InlineData("Cultures:Default", "de")]
    [InlineData("PreviewOrigins:0", "https://www.example.com/preview")]
    public async Task AValueTheAdminWouldRefuseStopsTheHost(string key, string value)
    {
        var settings = AdminSettings();
        settings["FluentCms:Admin:Cultures:Offered:0"] = "en";
        settings[$"FluentCms:Admin:{key}"] = value;

        var exception = await Should.ThrowAsync<Exception>(() => _host.StartAsync(settings));

        var refusal = Refusals(exception).ShouldHaveSingleItem();
        refusal.Code.ShouldBe("FCMS1101");
        refusal.Message.ShouldContain($"FluentCms:Admin:{key.Split(':')[0]}");
    }

    // A frame of the admin's own origin with scripts and the same origin allowed would reach its sessionStorage.
    [Fact]
    public async Task APreviewOriginThatIsTheAdminsOwnStopsTheHost()
    {
        var settings = AdminSettings();
        settings["FluentCms:Admin:PreviewOrigins:0"] = "https://localhost/";

        var exception = await Should.ThrowAsync<Exception>(() => _host.StartAsync(settings));

        var refusal = Refusals(exception).ShouldHaveSingleItem();
        refusal.Code.ShouldBe("FCMS1101");
        refusal.Message.ShouldBe("FCMS1101: FluentCms:Admin:PreviewOrigins:0 is 'https://localhost/', the admin's own origin; serve the preview pages from another origin.");
    }

    // A logo whose URL carries a query is allowed by img-src, as config.json carries it.
    [Fact]
    public async Task TheOriginsOfTheLogoAndTheFaviconAreAllowedWhateverTheirQuery()
    {
        var settings = AdminSettings();
        settings["FluentCms:Admin:Branding:LogoUrl"] = "https://cdn.example.com/logo.svg?v=2";
        settings["FluentCms:Admin:Branding:FaviconUrl"] = "https://icons.example.net/f.ico";
        await _host.StartAsync(settings);
        using var client = _host.Client();

        using var page = await client.GetAsync(new Uri("/admin/", UriKind.Relative), Token);
        page.Headers.GetValues("Content-Security-Policy").ShouldHaveSingleItem()
            .ShouldContain("img-src 'self' data: blob: https://localhost https://cdn.example.com https://icons.example.net;");
    }

    // The issuer of a request in Development under a path base has no trailing slash, as discovery writes it.
    [Fact]
    public async Task InDevelopmentUnderAPathBaseTheAuthorityIsTheIssuerOfDiscovery()
    {
        var settings = Operator.Settings();
        settings["FluentCms:Admin:Enabled"] = "true";
        settings["FluentCms:ReverseProxy:PathBase"] = "/cms";
        await _host.StartAsync(settings, environment: "Development");
        using var client = _host.Client(new Uri("https://localhost:5001/"));

        using var config = await client.GetAsync(new Uri("/cms/admin/config.json", UriKind.Relative), Token);
        var (_, configuration) = await HostUnderTest.ReadAsync(config);
        using var discovery = await client.GetAsync(new Uri("/cms/.well-known/openid-configuration", UriKind.Relative), Token);
        var (_, metadata) = await HostUnderTest.ReadAsync(discovery);

        configuration!["authority"]!.GetValue<string>().ShouldBe("https://localhost:5001/cms");
        metadata!["issuer"]!.GetValue<string>().ShouldBe(configuration["authority"]!.GetValue<string>());
        configuration["apiBaseUrl"]!.GetValue<string>().ShouldBe("https://localhost:5001/cms");
    }

    // An address the admin's reader would refuse stops the host, rather than the page failing or the admin showing its error.
    [Theory]
    [InlineData("FluentCms:PublicOrigin", "https://mÃ¼nchen.example", "xn--")]
    [InlineData("FluentCms:ReverseProxy:PathBase", "/c(m)s", "FluentCms:ReverseProxy:PathBase")]
    [InlineData("FluentCms:ReverseProxy:PathBase", "/a|b", "FluentCms:ReverseProxy:PathBase")]
    [InlineData("FluentCms:ReverseProxy:PathBase", "/a%20b", "FluentCms:ReverseProxy:PathBase")]
    public async Task AnAddressTheAdminCannotTakeStopsTheHost(string key, string value, string named)
    {
        var settings = AdminSettings();
        settings[key] = value;

        var exception = await Should.ThrowAsync<Exception>(() => _host.StartAsync(settings));

        var refusal = Refusals(exception).ShouldHaveSingleItem();
        refusal.Code.ShouldBe("FCMS1101");
        refusal.Message.ShouldContain(key);
        refusal.Message.ShouldContain(named);
    }

    // The app's index.html is checked when the admin is mapped, and so is the manifest of its files.
    [Theory]
    [InlineData("<html><head><title>No base</title></head></html>", true, "0 <base href> elements")]
    [InlineData("<html><head><base href=\"/\" /><base href=\"/x/\" /></head></html>", true, "2 <base href> elements")]
    [InlineData("<html><head><base href=\"/\" /></head></html>", false, "no manifest of static web assets")]
    [InlineData(null, true, "found no index.html")]
    public async Task AnAppTheHostCannotServeStopsItWhenTheAdminIsMapped(string? index, bool manifest, string message)
    {
        var root = Directory.CreateTempSubdirectory("fluentcms-admin-bare-");
        try
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, "wwwroot"));
            if (index is not null)
            {
                await File.WriteAllTextAsync(Path.Combine(root.FullName, "wwwroot", "index.html"), index, Token);
            }
            var path = Path.Combine(root.FullName, "app.staticwebassets.endpoints.json");
            if (manifest)
            {
                await File.WriteAllTextAsync(path, """{ "Version": 1, "ManifestType": "Build", "Endpoints": [] }""", Token);
            }

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development", ContentRootPath = root.FullName });
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["FluentCms:Admin:Enabled"] = "true" });
            builder.Services.AddFluentCms(cms => cms.UseSqlite($"Data Source={Path.Combine(root.FullName, "cms.db")}").AddAdmin());
            await using var app = builder.Build();
            app.UseFluentCms();

            var refusal = Should.Throw<FluentCmsConfigurationException>(() => app.MapFluentCmsAdmin(path));

            refusal.Code.ShouldBe("FCMS1103");
            refusal.Message.ShouldContain(message);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            root.Delete(recursive: true);
        }
    }

    private static Dictionary<string, string?> AdminSettings()
    {
        var settings = Operator.Settings();
        settings["FluentCms:PublicOrigin"] = "https://localhost";
        settings["FluentCms:Admin:Enabled"] = "true";
        return settings;
    }

    // The authorization server refuses a request it cannot send back to the client on its error page, never the login.
    private static void ShouldBeRefused(HttpResponseMessage response, string parameter)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = response.Headers.Location!.OriginalString;
        location.ShouldStartWith("/account/error?error=invalid_request");
        Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri("https://localhost" + location).Query)["error_description"].ToString().ShouldContain($"'{parameter}'");
    }

    private static List<FluentCmsConfigurationException> Refusals(Exception exception) => exception switch
    {
        AggregateException aggregate => [.. aggregate.InnerExceptions.SelectMany(Refusals)],
        FluentCmsConfigurationException refusal => [refusal],
        _ => throw new ShouldAssertException($"Not a configuration refusal: {exception}"),
    };

    private static Uri Authorize(string redirectUri, bool pkce, string scope = "openid profile email offline_access management")
    {
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = "fluentcms-admin",
            ["response_type"] = "code",
            ["redirect_uri"] = redirectUri,
            ["scope"] = scope,
            ["state"] = "admin",
            ["nonce"] = "admin",
        };
        if (pkce)
        {
            var verifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
            query["code_challenge"] = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            query["code_challenge_method"] = "S256";
        }

        return new Uri("/connect/authorize" + QueryString.Create(query), UriKind.Relative);
    }

    // index.html as the build of the app wrote it.
    private string BuiltIndex()
    {
        var file = _host.App.Services.GetRequiredService<IWebHostEnvironment>().WebRootFileProvider.GetFileInfo("index.html");
        file.Exists.ShouldBeTrue();
        using var reader = new StreamReader(file.CreateReadStream());
        return reader.ReadToEnd();
    }

    // A route of the app's _framework folder from the host's manifest of static web assets.
    private static string FrameworkRoute(string extension, bool immutable)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "FluentCMS.Host.staticwebassets.endpoints.json")));
        return manifest.RootElement.GetProperty("Endpoints").EnumerateArray()
            .Where(endpoint => endpoint.GetProperty("Route").GetString() is { } route && route.StartsWith("_framework/", StringComparison.Ordinal) && route.EndsWith(extension, StringComparison.Ordinal))
            .Where(endpoint => endpoint.GetProperty("ResponseHeaders").EnumerateArray()
                .Any(header => header.GetProperty("Name").GetString() == "Cache-Control" && header.GetProperty("Value").GetString()!.Contains("immutable", StringComparison.Ordinal)) == immutable)
            .Select(endpoint => endpoint.GetProperty("Route").GetString()!)
            .First();
    }
}
