using System.Net;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Hosting;

public sealed class ReverseProxyTests(CmsWebHosts hosts) : IClassFixture<CmsWebHosts>, IAsyncDisposable
{
    private const string Proxy = "10.0.0.1";
    private const string Section = "FluentCms:ReverseProxy:";

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly CmsWebHost _host = new();

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Theory]
    [InlineData("/api/delivery/v1")]
    [InlineData("/api/management/v1")]
    [InlineData("/connect")]
    [InlineData("/account")]
    [InlineData("/health")]
    public async Task EveryRouteGroupIsServedUnderThePathBaseAndNotWithout(string prefix)
    {
        var client = await Start(Environments.Development, new() { [Section + "PathBase"] = "/cms" });

        (await Get(client, $"/cms{prefix}/probe")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Get(client, $"{prefix}/probe")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task WithoutAPathBaseTheRoutesAreAtTheRoot()
    {
        var client = await Start(Environments.Development);

        (await Get(client, "/api/delivery/v1/probe")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Get(client, "/cms/api/delivery/v1/probe")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TheHostsOwnRoutesAreNotHeldToThePathBase()
    {
        var client = await Start(Environments.Development, new() { [Section + "PathBase"] = "/cms" });

        (await Get(client, "/host")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TheHttpsRedirectKeepsThePathBase()
    {
        var client = await Start(
            Environments.Development,
            new()
            {
                [Section + "PathBase"] = "/cms",
                [Section + "HttpsRedirection:Enabled"] = "true",
                [Section + "HttpsRedirection:Port"] = "8443",
            });

        using var response = await Get(client, "/cms/api/delivery/v1/probe");

        response.StatusCode.ShouldBe(HttpStatusCode.TemporaryRedirect);
        response.Headers.Location!.ToString().ShouldBe("https://localhost:8443/cms/api/delivery/v1/probe");
    }

    [Fact]
    public async Task ForwardedHeadersFromAKnownProxySetTheSchemeAndTheClientAddress()
    {
        var client = await Start(Environments.Development, Forwarding());

        var seen = await Seen(client, (HeaderNames.TestAddress, Proxy), (HeaderNames.ForwardedFor, "203.0.113.9"), (HeaderNames.ForwardedProto, "https"));

        seen.ShouldBe("https 203.0.113.9 localhost");
    }

    [Fact]
    public async Task ForwardedHeadersFromAnUnknownAddressAreIgnored()
    {
        var client = await Start(Environments.Development, Forwarding());

        var seen = await Seen(client, (HeaderNames.TestAddress, "10.0.0.2"), (HeaderNames.ForwardedFor, "203.0.113.9"), (HeaderNames.ForwardedProto, "https"));

        seen.ShouldBe("http 10.0.0.2 localhost");
    }

    [Fact]
    public async Task ForwardedHeadersAreOffByDefault()
    {
        var client = await Start(Environments.Development);

        var seen = await Seen(client, (HeaderNames.TestAddress, Proxy), (HeaderNames.ForwardedFor, "203.0.113.9"), (HeaderNames.ForwardedProto, "https"));

        seen.ShouldBe($"http {Proxy} localhost");
    }

    [Fact]
    public async Task AKnownNetworkIsTrustedLikeAProxy()
    {
        var client = await Start(
            Environments.Development,
            new() { [Section + "ForwardedHeaders:Enabled"] = "true", [Section + "ForwardedHeaders:KnownNetworks:0"] = "10.0.0.0/24" });

        var seen = await Seen(client, (HeaderNames.TestAddress, "10.0.0.77"), (HeaderNames.ForwardedFor, "203.0.113.9"));

        seen.ShouldBe("http 203.0.113.9 localhost");
    }

    [Fact]
    public async Task TheHostHeaderIsForwardedOnlyWhenAskedFor()
    {
        var withHost = await Start(Environments.Development, Forwarding(host: true));
        (await Seen(withHost, (HeaderNames.TestAddress, Proxy), (HeaderNames.ForwardedHost, "cms.example.com"))).ShouldBe("http 10.0.0.1 cms.example.com");

        await using var other = new CmsWebHost();
        var without = await Start(Environments.Development, Forwarding(), other);
        (await Seen(without, (HeaderNames.TestAddress, Proxy), (HeaderNames.ForwardedHost, "cms.example.com"))).ShouldBe("http 10.0.0.1 localhost");
    }

    [Fact]
    public async Task AForwardedHostOutsideAllowedHostsIsNotApplied()
    {
        var settings = Forwarding(host: true);
        settings["AllowedHosts"] = "cms.example.com;localhost";
        var client = await Start(Environments.Production, settings);

        (await Seen(client, (HeaderNames.TestAddress, Proxy), (HeaderNames.ForwardedHost, "evil.example"), (HeaderNames.ForwardedProto, "https"))).ShouldBe("https 10.0.0.1 localhost");
        (await Seen(client, (HeaderNames.TestAddress, Proxy), (HeaderNames.ForwardedHost, "cms.example.com"))).ShouldBe("http 10.0.0.1 cms.example.com");
    }

    [Fact]
    public async Task AForwardedHostIsAppliedWhenAllowedHostsAcceptsAnyHost()
    {
        var settings = Forwarding(host: true);
        settings["AllowedHosts"] = "*";
        var client = await Start(Environments.Production, settings);

        (await Seen(client, (HeaderNames.TestAddress, Proxy), (HeaderNames.ForwardedHost, "cms.example.com"))).ShouldBe("http 10.0.0.1 cms.example.com");
    }

    [Theory]
    [InlineData("Production", "https://cms.example.com", null)]
    [InlineData("Production", "https://cms.example.com/", null)]
    [InlineData("Production", "https://cms.example.com:8443/cms", "/cms")]
    [InlineData("Production", "https://cms.example.com/caf%C3%A9", "/café")]
    [InlineData("Development", "http://localhost:5000", null)]
    public async Task AValidPublicOriginStarts(string environment, string origin, string? pathBase)
    {
        var client = await Start(environment, new() { ["FluentCms:PublicOrigin"] = origin, [Section + "PathBase"] = pathBase });

        (await Get(client, $"{pathBase}/api/delivery/v1/probe")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("Production", "cms.example.com", null)]
    [InlineData("Production", "/cms", null)]
    [InlineData("Production", "ftp://cms.example.com", null)]
    [InlineData("Production", "http://cms.example.com", null)]
    [InlineData("Staging", "http://cms.example.com", null)]
    [InlineData("Production", "https://cms.example.com/?site=1", null)]
    [InlineData("Production", "https://cms.example.com/#top", null)]
    [InlineData("Production", "https://user@cms.example.com", null)]
    [InlineData("Production", "https://cms.example.com/cms", null)]
    [InlineData("Production", "https://cms.example.com/other", "/cms")]
    public async Task ABadPublicOriginStopsTheHost(string environment, string origin, string? pathBase)
    {
        var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() =>
            Start(environment, new() { ["FluentCms:PublicOrigin"] = origin, [Section + "PathBase"] = pathBase }));

        exception.Code.ShouldBe("FCMS1101");
        exception.Message.ShouldContain("FluentCms:PublicOrigin");
    }

    [Fact]
    public async Task TheRateLimitPartitionsByTheForwardedAddressFromAKnownProxy()
    {
        var settings = Forwarding();
        settings["FluentCms:RateLimits:Delivery:PermitLimit"] = "1";
        var client = await Start(Environments.Development, settings);

        (await Status(client, (HeaderNames.TestAddress, Proxy), (HeaderNames.ForwardedFor, "203.0.113.1"))).ShouldBe(HttpStatusCode.OK);
        (await Status(client, (HeaderNames.TestAddress, Proxy), (HeaderNames.ForwardedFor, "203.0.113.2"))).ShouldBe(HttpStatusCode.OK);
        (await Status(client, (HeaderNames.TestAddress, Proxy), (HeaderNames.ForwardedFor, "203.0.113.1"))).ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task TheRateLimitIgnoresTheForwardedAddressFromAnUnknownProxy()
    {
        var settings = Forwarding();
        settings["FluentCms:RateLimits:Delivery:PermitLimit"] = "1";
        var client = await Start(Environments.Development, settings);

        (await Status(client, (HeaderNames.TestAddress, "10.0.0.2"), (HeaderNames.ForwardedFor, "203.0.113.1"))).ShouldBe(HttpStatusCode.OK);
        (await Status(client, (HeaderNames.TestAddress, "10.0.0.2"), (HeaderNames.ForwardedFor, "203.0.113.2"))).ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task HttpsRedirectionIsOffByDefaultAndRedirectsWhenOn()
    {
        var off = await Start(Environments.Production);
        (await Get(off, "/api/delivery/v1/probe")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var other = new CmsWebHost();
        var on = await Start(Environments.Production, new() { [Section + "HttpsRedirection:Enabled"] = "true", [Section + "HttpsRedirection:Port"] = "443" }, other);
        using var redirect = await Get(on, "/api/delivery/v1/probe");

        redirect.StatusCode.ShouldBe(HttpStatusCode.TemporaryRedirect);
        redirect.Headers.Location!.ToString().ShouldBe("https://localhost/api/delivery/v1/probe");
    }

    [Fact]
    public async Task TheHttpsPortOfTheEnvironmentIsHonouredWhenNoPortIsSet()
    {
        var client = await Start(Environments.Production, new() { [Section + "HttpsRedirection:Enabled"] = "true", ["HTTPS_PORT"] = "5001" });

        using var redirect = await Get(client, "/api/delivery/v1/probe");

        redirect.StatusCode.ShouldBe(HttpStatusCode.TemporaryRedirect);
        redirect.Headers.Location!.ToString().ShouldBe("https://localhost:5001/api/delivery/v1/probe");
    }

    [Fact]
    public async Task ThePortOptionWinsOverTheEnvironment()
    {
        var client = await Start(Environments.Production, new() { [Section + "HttpsRedirection:Enabled"] = "true", [Section + "HttpsRedirection:Port"] = "8443", ["HTTPS_PORT"] = "5001" });

        using var redirect = await Get(client, "/api/delivery/v1/probe");

        redirect.Headers.Location!.ToString().ShouldBe("https://localhost:8443/api/delivery/v1/probe");
    }

    [Fact]
    public async Task APathRefusedForLackingThePathBaseIsAProblemWithTheSecurityHeaders()
    {
        var client = await Start(Environments.Development, new() { [Section + "PathBase"] = "/cms" });

        using var response = await Get(client, "/api/delivery/v1/probe");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        Header(response, "X-Content-Type-Options").ShouldBe("nosniff");
        (await response.Content.ReadAsStringAsync(Token)).ShouldContain("FCMS3104");
    }

    [Fact]
    public async Task APathOfAGroupThatNoEndpointAnswersIsHeldToThePathBaseToo()
    {
        var client = await Start(Environments.Development, new() { [Section + "PathBase"] = "/cms" });

        using var response = await Get(client, "/connect/token");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task HttpsRedirectionLeavesARequestTheProxyForwardedAsHttps()
    {
        var settings = Forwarding();
        settings[Section + "HttpsRedirection:Enabled"] = "true";
        settings[Section + "HttpsRedirection:Port"] = "443";
        var client = await Start(Environments.Production, settings);

        using var forwarded = await Get(client, "/api/delivery/v1/probe", (HeaderNames.TestAddress, Proxy), (HeaderNames.ForwardedProto, "https"));
        using var plain = await Get(client, "/api/delivery/v1/probe", (HeaderNames.TestAddress, Proxy));

        forwarded.StatusCode.ShouldBe(HttpStatusCode.OK);
        plain.StatusCode.ShouldBe(HttpStatusCode.TemporaryRedirect);
    }

    [Fact]
    public async Task HstsIsOnOutsideDevelopmentForHttpsRequests()
    {
        var client = await Start(Environments.Production);

        using var https = await Get(client, "https://app.example.com/api/delivery/v1/probe");
        using var http = await Get(client, "http://app.example.com/api/delivery/v1/probe");

        Header(https, "Strict-Transport-Security").ShouldBe("max-age=2592000");
        Header(http, "Strict-Transport-Security").ShouldBeNull();
    }

    [Fact]
    public async Task HstsIsOffInDevelopmentUnlessTurnedOn()
    {
        var off = await Start(Environments.Development);
        using var response = await Get(off, "https://app.example.com/api/delivery/v1/probe");
        Header(response, "Strict-Transport-Security").ShouldBeNull();

        await using var other = new CmsWebHost();
        var on = await Start(Environments.Development, new() { [Section + "Hsts:Enabled"] = "true" }, other);
        using var enabled = await Get(on, "https://app.example.com/api/delivery/v1/probe");
        Header(enabled, "Strict-Transport-Security").ShouldBe("max-age=2592000");
    }

    [Fact]
    public async Task HstsFollowsItsOptions()
    {
        var client = await Start(
            Environments.Production,
            new()
            {
                [Section + "Hsts:MaxAge"] = "365.00:00:00",
                [Section + "Hsts:IncludeSubDomains"] = "true",
                [Section + "Hsts:Preload"] = "true",
            });

        using var response = await Get(client, "https://app.example.com/api/delivery/v1/probe");

        Header(response, "Strict-Transport-Security").ShouldBe("max-age=31536000; includeSubDomains; preload");
    }

    [Fact]
    public async Task HstsCanBeTurnedOffInProduction()
    {
        var client = await Start(Environments.Production, new() { [Section + "Hsts:Enabled"] = "false" });

        using var response = await Get(client, "https://app.example.com/api/delivery/v1/probe");

        Header(response, "Strict-Transport-Security").ShouldBeNull();
    }

    private static Dictionary<string, string?> Forwarding(bool host = false) => new()
    {
        [Section + "ForwardedHeaders:Enabled"] = "true",
        [Section + "ForwardedHeaders:KnownProxies:0"] = Proxy,
        [Section + "ForwardedHeaders:ForwardHost"] = host ? "true" : "false",
    };

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.Single() : null;

    private static async Task<HttpResponseMessage> Get(HttpClient client, string path, params (string Name, string Value)[] headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path.StartsWith('/') ? new Uri(path, UriKind.Relative) : new Uri(path));
        foreach (var (name, value) in headers)
        {
            request.Headers.Add(name, value);
        }

        return await client.SendAsync(request, Token);
    }

    private static async Task<HttpStatusCode> Status(HttpClient client, params (string Name, string Value)[] headers)
    {
        using var response = await Get(client, "/api/delivery/v1/probe", headers);
        return response.StatusCode;
    }

    private static async Task<string> Seen(HttpClient client, params (string Name, string Value)[] headers)
    {
        using var response = await Get(client, "/api/delivery/v1/seen", headers);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync(Token);
    }

    // A test that names a host starts that one; a rate limit counts what each test sends, so a host with one is the test's
    // alone. Any other set-up is shared.
    private async Task<HttpClient> Start(string environment, Dictionary<string, string?>? settings = null, CmsWebHost? host = null)
    {
        var app = host is null && settings?.Keys.Any(key => key.StartsWith("FluentCms:RateLimits:", StringComparison.Ordinal)) != true
            ? await hosts.StartAsync(CmsWebHosts.SetUp(environment, settings), shared => StartAsync(shared, environment, settings))
            : await StartAsync(host ?? _host, environment, settings);
        return app.GetTestClient();
    }

    private static async Task<WebApplication> StartAsync(CmsWebHost host, string environment, Dictionary<string, string?>? settings)
    {
        var app = host.Build(
            environment,
            settings,
            builder =>
            {
                foreach (var group in Enum.GetValues<FluentCmsEndpointGroup>())
                {
                    builder.Services.AddFluentCmsEndpoints(group, routes => routes.MapGet("/probe", () => "ok"));
                }

                builder.Services.AddFluentCmsEndpoints(
                    FluentCmsEndpointGroup.Delivery,
                    routes => routes.MapGet("/seen", (HttpContext context) => $"{context.Request.Scheme} {context.Connection.RemoteIpAddress} {context.Request.Host.Host}"));
            },
            pipeline: app =>
            {
                app.Use((context, next) =>
                {
                    if (context.Request.Headers.TryGetValue(HeaderNames.TestAddress, out var address))
                    {
                        context.Connection.RemoteIpAddress = IPAddress.Parse(address.ToString());
                    }

                    return next(context);
                });
                app.UseFluentCms();
                app.MapGet("/host", () => "host");
            });

        await app.StartAsync(Token);
        return app;
    }

    private static class HeaderNames
    {
        public const string TestAddress = "X-Test-Ip";
        public const string ForwardedFor = "X-Forwarded-For";
        public const string ForwardedProto = "X-Forwarded-Proto";
        public const string ForwardedHost = "X-Forwarded-Host";
    }
}
