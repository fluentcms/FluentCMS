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
