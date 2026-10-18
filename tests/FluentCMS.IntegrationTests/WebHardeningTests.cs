using System.Net;
using System.Text.RegularExpressions;

namespace FluentCMS.IntegrationTests;

public sealed partial class WebHardeningTests
{
    [Fact]
    public async Task A_form_post_without_a_valid_antiforgery_token_is_rejected()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        await host.CreateFirstAdministratorAsync();

        var signIn = await host.Client.PostAsync("/sign-in", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "sign-in",
            ["Form.Email"] = AdminHost.Owner.Email,
            ["Form.Password"] = AdminHost.Owner.Password,
        }));

        Assert.Equal(HttpStatusCode.BadRequest, signIn.StatusCode);
        Assert.False(host.IsSignedIn);

        await host.SignInAsync(AdminHost.Owner);
        var signOut = await host.Client.PostAsync("/sign-out", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "sign-out",
            ["__RequestVerificationToken"] = "forged",
        }));

        Assert.Equal(HttpStatusCode.BadRequest, signOut.StatusCode);
        Assert.True(host.IsSignedIn);
    }

    [Fact]
    public async Task Sign_in_posts_beyond_the_limit_are_throttled_while_the_page_stays_reachable()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install, settings: new Dictionary<string, string?>
        {
            ["SignInRateLimit:PermitLimit"] = "3",
            ["SignInRateLimit:Window"] = "00:01:00",
        });
        await host.CreateFirstAdministratorAsync();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            Assert.Equal(HttpStatusCode.OK, (await host.SignInAsync(AdminHost.Owner.Email, "Wr0ng!Passw0rd")).StatusCode);
        }

        var throttled = await host.SignInAsync(AdminHost.Owner);

        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        Assert.NotNull(throttled.Headers.RetryAfter);
        Assert.Contains("Too many attempts", await throttled.Content.ReadAsStringAsync());
        Assert.False(host.IsSignedIn);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/sign-in")).StatusCode);
    }

    [Fact]
    public async Task Every_response_carries_the_content_security_policy_and_the_sign_in_page_loads_its_assets_anonymously()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        await host.CreateFirstAdministratorAsync();

        var signIn = await host.Client.GetAsync("/sign-in");
        var html = await signIn.Content.ReadAsStringAsync();
        var assets = AssetReference().Matches(html).Select(match => "/" + match.Groups[1].Value).ToList();
        var responses = new List<HttpResponseMessage> { signIn, await host.Client.GetAsync("/health"), await host.Client.GetAsync("/no-such-page") };

        Assert.Contains(assets, asset => asset.Contains("app.generated"));
        Assert.Contains(assets, asset => asset.Contains("blazor.web"));
        foreach (var asset in assets)
        {
            var response = await host.Client.GetAsync(asset);
            var body = await response.Content.ReadAsByteArrayAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{asset} answered {response.StatusCode}.");
            Assert.True(body.Length > 0, $"{asset} answered an empty body.");

            if (asset.EndsWith(".css", StringComparison.Ordinal))
            {
                Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
            }

            responses.Add(response);
        }

        Assert.Equal(HttpStatusCode.NotFound, responses[2].StatusCode);
        Assert.All(responses, response =>
        {
            var policy = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
            Assert.Contains("default-src 'self'", policy);
            Assert.Contains("script-src 'self'", policy);
            Assert.Contains("frame-ancestors 'none'", policy);
            Assert.Contains("object-src 'none'", policy);
            Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        });
    }

    [Fact]
    public async Task A_cookie_issued_before_a_restart_is_still_valid_after_it()
    {
        using var install = new TemporaryInstall();
        var cookies = new System.Net.CookieContainer();

        await using (var before = await AdminHost.StartAsync(install, cookies: cookies))
        {
            await before.CreateFirstAdministratorAsync();
            await before.SignInAsync(AdminHost.Owner);
            Assert.True(before.IsSignedIn);
        }

        Assert.NotEmpty(Directory.GetFiles(install.KeysDirectory, "key-*.xml"));

        await using var after = await AdminHost.StartAsync(install, cookies: cookies);
        var home = await after.Client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        Assert.Contains(">Owner</span>", await home.Content.ReadAsStringAsync());
    }

    [GeneratedRegex("(?:href|src)=\"(_(?:content|framework)/[^\"]+)\"")]
    private static partial Regex AssetReference();
}
