using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentCMS.Api;
using FluentCMS.Core.Schema;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FluentCMS.IntegrationTests.Delivery;

public sealed class DeliveryHardeningTests
{
    [Fact]
    public async Task Requests_beyond_the_limit_get_a_problem_with_retry_after_while_the_admin_stays_reachable()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install, settings: new Dictionary<string, string?>
        {
            ["Delivery:RateLimit:PermitLimit"] = "3",
            ["Delivery:RateLimit:Window"] = "00:01:00",
        });

        for (var request = 0; request < 3; request++)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync($"/api/v1/entries/{Guid.CreateVersion7()}")).StatusCode);
        }

        var throttled = await host.Client.GetAsync("/api/v1/index");
        var llms = await host.Client.GetAsync("/llms.txt");

        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        Assert.NotNull(throttled.Headers.RetryAfter);
        Assert.Equal("application/problem+json", throttled.Content.Headers.ContentType?.MediaType);
        Assert.Equal(DeliveryRateLimit.RateLimited, (string?)(await throttled.Content.ReadFromJsonAsync<JsonObject>())!["errors"]![0]!["code"]);
        Assert.Equal(HttpStatusCode.TooManyRequests, llms.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/health")).StatusCode);
    }

    // Behind a trusted proxy each client has its own limit; without one, X-Forwarded-For is ignored and cannot pick a fresh bucket.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Only_a_trusted_proxy_names_the_client_a_limit_counts(bool trusted)
    {
        using var install = new TemporaryInstall();
        var settings = new Dictionary<string, string?>
        {
            ["Delivery:RateLimit:PermitLimit"] = "3",
            ["Delivery:RateLimit:Window"] = "00:01:00",
        };
        if (trusted)
        {
            settings["ReverseProxy:KnownProxies:0"] = "127.0.0.1";
        }

        await using var host = await AdminHost.StartAsync(install, settings: settings);

        async Task<HttpStatusCode> FromAsync(string client)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/index");
            request.Headers.Add("X-Forwarded-For", client);
            return (await host.Client.SendAsync(request)).StatusCode;
        }

        var sixClients = new List<HttpStatusCode>();
        for (var client = 1; client <= 6; client++)
        {
            sixClients.Add(await FromAsync($"203.0.113.{client}"));
        }

        if (trusted)
        {
            Assert.All(sixClients, status => Assert.Equal(HttpStatusCode.OK, status));
            Assert.Equal(HttpStatusCode.OK, await FromAsync("203.0.113.1"));
            Assert.Equal(HttpStatusCode.OK, await FromAsync("203.0.113.1"));
            Assert.Equal(HttpStatusCode.TooManyRequests, await FromAsync("203.0.113.1"));
        }
        else
        {
            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK], sixClients.Take(3));
            Assert.All(sixClients.Skip(3), status => Assert.Equal(HttpStatusCode.TooManyRequests, status));
        }
    }

    [Fact]
    public async Task A_proxy_that_is_not_an_address_stops_the_host()
    {
        using var install = new TemporaryInstall();

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => AdminHost.StartAsync(install, settings: new Dictionary<string, string?>
        {
            ["ReverseProxy:KnownProxies:0"] = "proxy.example",
            ["ReverseProxy:KnownNetworks:0"] = "10.0.0.0/33",
        }));

        Assert.Contains("ReverseProxy__KnownProxies has 'proxy.example'", exception.Message);
        Assert.Contains("ReverseProxy__KnownNetworks has '10.0.0.0/33'", exception.Message);
    }

    [Theory]
    [InlineData("2001:db8:1:2::5", "2001:db8:1:2:ffff::9", true)]
    [InlineData("2001:db8:1:2::5", "2001:db8:1:3::5", false)]
    [InlineData("::ffff:203.0.113.7", "203.0.113.7", true)]
    [InlineData("203.0.113.7", "203.0.113.8", false)]
    public void An_ipv6_client_is_counted_by_its_64_prefix(string first, string second, bool same)
    {
        Assert.Equal(same, DeliveryRateLimit.ClientKey(IPAddress.Parse(first)) == DeliveryRateLimit.ClientKey(IPAddress.Parse(second)));
    }

    [Fact]
    public void Each_client_address_has_its_own_limit()
    {
        var services = new ServiceCollection()
            .AddSingleton(Options.Create(new DeliveryRateLimitOptions { PermitLimit = 1 }))
            .BuildServiceProvider();
        var policy = new DeliveryRateLimit();

        HttpContext From(string address)
        {
            var context = new DefaultHttpContext { RequestServices = services };
            context.Connection.RemoteIpAddress = IPAddress.Parse(address);
            return context;
        }

        var first = policy.GetPartition(From("203.0.113.7"));
        var again = policy.GetPartition(From("203.0.113.7"));
        var other = policy.GetPartition(From("198.51.100.2"));

        Assert.Equal(first.PartitionKey, again.PartitionKey);
        Assert.NotEqual(first.PartitionKey, other.PartitionKey);
    }

    [Theory]
    [InlineData("Product-Page")]
    [InlineData("product_page")]
    [InlineData("a%20b")]
    public async Task A_type_that_cannot_be_a_slug_is_not_found_and_responses_are_not_sniffed(string type)
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);

        var response = await host.Client.GetAsync($"/api/v1/content/{type}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(SchemaErrorCodes.ContentTypeNotFound, (string?)(await response.Content.ReadFromJsonAsync<JsonObject>())!["errors"]![0]!["code"]);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("default-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task A_limit_no_request_can_meet_stops_the_host()
    {
        using var install = new TemporaryInstall();

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(
            () => AdminHost.StartAsync(install, settings: new Dictionary<string, string?> { ["Delivery:RateLimit:PermitLimit"] = "0" }));

        Assert.Contains("Delivery__RateLimit__PermitLimit", exception.Message);
    }
}
