using System.Net;
using FluentCMS.Core.DependencyInjection;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Hosting;

// The antiforgery cookie of the hosted pages is Secure, so a page asked for over plain http is refused with a page that
// says so, in every environment and whether or not the public origin is set; it used to fail with a 500 while it rendered
// its form (#3159).
public sealed class HostedPageHttpsTests(CmsWebHosts hosts) : IClassFixture<CmsWebHosts>, IAsyncDisposable
{
    private const string Proxy = "10.0.0.1";
    private const string TestAddress = "X-Test-Ip";
    private const string Section = "FluentCms:ReverseProxy:ForwardedHeaders:";

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly CmsWebHost _host = new();

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Theory]
    [InlineData("Development", null, "/account/login")]
    [InlineData("Development", "https://cms.example.com", "/account/login")]
    [InlineData("Production", null, "/account/login")]
    [InlineData("Production", "https://cms.example.com", "/account/login")]
    [InlineData("Production", null, "/account/members/login")]
    [InlineData("Production", null, "/account/forgot-password")]
    public async Task APageAskedForOverHttpIsRefusedWithAPageThatNamesTheCause(string environment, string? publicOrigin, string path)
    {
        var client = await Start(environment, publicOrigin is null ? [] : new() { ["FluentCms:PublicOrigin"] = publicOrigin });

        using var response = await Get(client, "http://localhost" + path);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        string.Join(", ", response.Headers.GetValues("Content-Security-Policy")).ShouldContain("frame-ancestors 'none'");
        var html = await response.Content.ReadAsStringAsync(Token);
        html.ShouldContain("FCMS3108");
        html.ShouldContain("https");
        html.ShouldNotContain("FCMS3199");
    }

    [Fact]
    public async Task APostOverHttpIsRefusedTheSameWay()
    {
        var client = await Start(Environments.Production);

        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Input.UserName"] = "someone" });
        using var response = await client.PostAsync(new Uri("http://localhost/account/login"), content, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(Token)).ShouldContain("FCMS3108");
    }

    [Fact]
    public async Task APageAskedForOverHttpsRendersItsForm()
    {
        var client = await Start(Environments.Production);

        using var response = await Get(client, "https://localhost/account/login");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Token)).ShouldContain("__RequestVerificationToken");
    }

    [Fact]
    public async Task HttpsForwardedByAKnownProxyRendersTheForm()
    {
        var client = await Start(Environments.Production, Forwarding());

        using var response = await Get(client, "http://localhost/account/login", (TestAddress, Proxy), ("X-Forwarded-Proto", "https"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Token)).ShouldContain("__RequestVerificationToken");
    }

    // The scheme of a proxy the host does not trust is not applied, so the request is still http, and it is refused rather
    // than sent to https, which through that proxy would come back as http again.
    [Fact]
    public async Task HttpsForwardedByAnUnknownAddressIsStillRefused()
    {
        var client = await Start(Environments.Production, Forwarding());

        using var response = await Get(client, "http://localhost/account/login", (TestAddress, "10.0.0.2"), ("X-Forwarded-Proto", "https"));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Headers.Location.ShouldBeNull();
        (await response.Content.ReadAsStringAsync(Token)).ShouldContain("FCMS3108");
    }

    [Fact]
    public async Task AHostThatRelaxesTheCookieGetsThePagesOverHttp()
    {
        var app = _host.Build(
            Environments.Development,
            configure: builder => builder.Services.Configure<AntiforgeryOptions>(options => options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest));
        await app.StartAsync(Token);

        using var response = await Get(app.GetTestClient(), "http://localhost/account/login");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static Dictionary<string, string?> Forwarding() => new()
    {
        [Section + "Enabled"] = "true",
        [Section + "KnownProxies:0"] = Proxy,
    };

    private async Task<HttpClient> Start(string environment, Dictionary<string, string?>? settings = null)
    {
        var app = await hosts.StartAsync(CmsWebHosts.SetUp(environment, settings), async host =>
        {
            var app = host.Build(
                environment,
                settings,
                pipeline: app =>
                {
                    app.Use((context, next) =>
                    {
                        if (context.Request.Headers.TryGetValue(TestAddress, out var address))
                        {
                            context.Connection.RemoteIpAddress = IPAddress.Parse(address.ToString());
                        }

                        return next(context);
                    });
                    app.UseFluentCms();
                });
            await app.StartAsync(Token);
            return app;
        });
        return app.GetTestClient();
    }

    private static async Task<HttpResponseMessage> Get(HttpClient client, string url, params (string Name, string Value)[] headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(url));
        foreach (var (name, value) in headers)
        {
            request.Headers.Add(name, value);
        }

        return await client.SendAsync(request, Token);
    }
}
